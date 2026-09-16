using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using DSO.Core.Evoker;

namespace DSO.Core.Evoker.Extend
{
    /// <summary>
    /// Var olan bir interface'i DynamicClass'ın ürettiği tipe implement etme desteği.
    ///
    /// FAZ 1: property'ler (get/set) - otomatik şemaya eklenir.
    /// FAZ 3: gerçek davranışlı METOTLAR ARTIK DA DESTEKLENİYOR - otomatik AddMethod ile
    /// şemaya eklenir, gövdesi ise BOŞTUR: SetMethod(...) ile bir delegate atamanız gerekir
    /// (atamazsanız çağrıldığında InvalidOperationException alırsınız - sessiz NullReferenceException
    /// değil). Bu, gerçek bir "kod sentezi" değil, Castle DynamicProxy'nin interceptor mantığının
    /// basitleştirilmiş bir versiyonu: metot çağrıldığında sizin verdiğiniz delegate çalışır.
    /// </summary>
    public static class DynamicClassInterfaceExtensions
    {
        /// <summary>
        /// dc'nin ürettiği tipin TInterface'i implement etmesini sağlar. Property'ler VE
        /// metotlar otomatik şemaya eklenir. Metotları SetMethod(...) ile doldurmayı unutmayın.
        /// </summary>
        public static DynamicClass Implement<TInterface>(this DynamicClass dc) where TInterface : class
        {
            Type ifaceType = typeof(TInterface);

            if (!ifaceType.IsInterface)
            {
                throw new ArgumentException(
                    $"[Extend] '{ifaceType.Name}' bir interface değil. Implement<T>() sadece interface'lerle kullanılabilir.");
            }

            var properties = ifaceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var methods = ifaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName) // get_X/set_X'leri (property accessor'ları) hariç tut
                .ToList();

            if (properties.Length == 0 && methods.Count == 0)
            {
                throw new ArgumentException($"[Extend] '{ifaceType.Name}' hiç üye içermiyor - implement edecek bir şey yok.");
            }

            foreach (var p in properties)
            {
                dc.AddProperty(p.Name, p.PropertyType);
            }

            foreach (var m in methods)
            {
                Type delegateType = DelegateTypeResolver.Resolve(m);
                DelegateTypeResolver.AddMethodDynamic(dc, m.Name, delegateType);
            }

            dc.WithTypeConfigurator((typeBuilder, members) =>
            {
                typeBuilder.AddInterfaceImplementation(ifaceType);

                foreach (var p in properties)
                {
                    if (!members.Properties.TryGetValue(p.Name, out var accessors))
                    {
                        throw new InvalidOperationException($"[Extend] '{p.Name}' property'si için üretilen get/set metotları bulunamadı.");
                    }

                    var ifaceGetter = p.GetGetMethod();
                    if (ifaceGetter != null) typeBuilder.DefineMethodOverride(accessors.Get, ifaceGetter);

                    var ifaceSetter = p.GetSetMethod();
                    if (ifaceSetter != null) typeBuilder.DefineMethodOverride(accessors.Set, ifaceSetter);
                }

                foreach (var m in methods)
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

        /// <summary>
        /// dc.RawInstance'ı TInterface'e cast eder. Implement&lt;TInterface&gt;() çağrılmadıysa
        /// InvalidCastException.
        /// </summary>
        public static TInterface As<TInterface>(this DynamicClass dc) where TInterface : class
        {
            object instance = dc.RawInstance;
            if (instance is TInterface typed)
            {
                return typed;
            }

            throw new InvalidCastException(
                $"[Extend] '{dc.Type.Name}' tipi '{typeof(TInterface).Name}' interface'ini implement etmiyor. " +
                $"Implement<{typeof(TInterface).Name}>() çağırmayı unuttunuz mu?");
        }
    }

    /// <summary>
    /// Bir MethodInfo'nun imzasına (parametre tipleri + dönüş tipi) TAM UYAN bir
    /// Func&lt;...&gt;/Action&lt;...&gt; delegate tipini runtime'da çözer. DynamicClass.
    /// AddMethod&lt;TDelegate&gt;() generic bir metot olduğu için, TDelegate compile-time'da
    /// bilinmediğinde (interface'i reflection ile geziyoruz) reflection ile generic metodu
    /// runtime'da somutlaştırmamız gerekiyor.
    /// </summary>
    internal static class DelegateTypeResolver
    {
        public static Type Resolve(MethodInfo method)
        {
            Type[] paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            Type returnType = method.ReturnType;

            if (returnType == typeof(void))
            {
                return paramTypes.Length == 0 ? typeof(Action) : Expression.GetActionType(paramTypes);
            }

            Type[] funcTypeArgs = paramTypes.Concat(new[] { returnType }).ToArray();
            return Expression.GetFuncType(funcTypeArgs);
        }

        private static readonly MethodInfo AddMethodGenericDefinition =
            typeof(DynamicClass).GetMethod(nameof(DynamicClass.AddMethod))!;

        public static void AddMethodDynamic(DynamicClass dc, string methodName, Type delegateType)
        {
            MethodInfo generic = AddMethodGenericDefinition.MakeGenericMethod(delegateType);
            generic.Invoke(dc, new object[] { methodName });
        }
    }
}