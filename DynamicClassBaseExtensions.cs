using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DSO.Core.Evoker;

namespace DSO.Core.Evoker.Extend
{
    /// <summary>
    /// Var olan bir sınıftan türeme desteği.
    ///
    /// FAZ 2: abstract property'ler - otomatik şemaya eklenir.
    /// FAZ 3: abstract METOTLAR ARTIK DA DESTEKLENİYOR - otomatik AddMethod ile şemaya
    /// eklenir, SetMethod(...) ile doldurmanız gerekir.
    ///
    /// KAPSAM (hâlâ dar): Base class SEALED olamaz, erişilebilir (public/protected)
    /// PARAMETRESİZ bir constructor'ı olmak zorunda. Somut (abstract olmayan) metotlar/
    /// property'ler OLDUĞU GİBİ miras alınır, CLR'ın normal inheritance mekanizmasıyla.
    /// </summary>
    public static class DynamicClassBaseExtensions
    {
        public static DynamicClass Extend<TBase>(this DynamicClass dc) where TBase : class
        {
            Type baseType = typeof(TBase);

            if (baseType.IsInterface)
            {
                throw new ArgumentException(
                    $"[Extend] '{baseType.Name}' bir interface, sınıf değil. Interface için Implement<T>() kullanın.");
            }

            if (baseType.IsSealed)
            {
                throw new NotSupportedException($"[Extend] '{baseType.Name}' sealed bir sınıf, miras alınamaz.");
            }

            var baseCtor = baseType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, Type.EmptyTypes, modifiers: null);

            if (baseCtor == null || !(baseCtor.IsPublic || baseCtor.IsFamily))
            {
                throw new NotSupportedException(
                    $"[Extend v2] '{baseType.Name}' sınıfının erişilebilir (public/protected) parametresiz " +
                    "bir constructor'ı yok. Bu sürüm sadece parametresiz-ctor'lu base class'ları destekliyor.");
            }

            var abstractProperties = baseType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => (p.GetGetMethod(true)?.IsAbstract ?? false) || (p.GetSetMethod(true)?.IsAbstract ?? false))
                .ToList();

            var abstractMethods = baseType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.IsAbstract && !m.IsSpecialName)
                .ToList();

            foreach (var p in abstractProperties)
            {
                dc.AddProperty(p.Name, p.PropertyType);
            }

            foreach (var m in abstractMethods)
            {
                Type delegateType = DelegateTypeResolver.Resolve(m);
                DelegateTypeResolver.AddMethodDynamic(dc, m.Name, delegateType);
            }

            dc.WithTypeConfigurator((typeBuilder, members) =>
            {
                // Property/metot emisyonundan SONRA (CreateType()'dan önce) çağrılıyor - bunun
                // gerçekten çalıştığını izole bir testle doğruladık.
                typeBuilder.SetParent(baseType);

                foreach (var p in abstractProperties)
                {
                    if (!members.Properties.TryGetValue(p.Name, out var accessors))
                    {
                        throw new InvalidOperationException($"[Extend] '{p.Name}' property'si için üretilen get/set metotları bulunamadı.");
                    }

                    var baseGetter = p.GetGetMethod(true);
                    if (baseGetter != null && baseGetter.IsAbstract) typeBuilder.DefineMethodOverride(accessors.Get, baseGetter);

                    var baseSetter = p.GetSetMethod(true);
                    if (baseSetter != null && baseSetter.IsAbstract) typeBuilder.DefineMethodOverride(accessors.Set, baseSetter);
                }

                foreach (var m in abstractMethods)
                {
                    if (!members.Methods.TryGetValue(m.Name, out var forwarder))
                    {
                        throw new InvalidOperationException($"[Extend] '{m.Name}' metodu için üretilen forwarder bulunamadı.");
                    }

                    typeBuilder.DefineMethodOverride(forwarder.Method, m);
                }
            });

            return dc;
        }
    }
}