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
        ///
        /// ÖNEMLİ KISIT: TInterface PUBLIC olmalıdır. Dinamik tip AYRI bir assembly'de üretiliyor
        /// (bkz. DynamicTypeFactory'nin paylaşımlı modülü) - internal bir interface'i implement
        /// etmeye çalışırsanız CreateType() aşamasında "attempting to implement an inaccessible
        /// interface" TypeLoadException alırsınız (InternalsVisibleTo ile bile pratik değildir,
        /// çünkü dinamik assembly'nin adı stabil/önceden bilinen bir şey değildir).
        /// </summary>
        public static DynamicClass Implement<TInterface>(this DynamicClass dc) where TInterface : class
        {
            Type ifaceType = typeof(TInterface);

            if (!ifaceType.IsInterface)
            {
                throw new ArgumentException(
                    $"[Extend] '{ifaceType.Name}' bir interface değil. Implement<T>() sadece interface'lerle kullanılabilir.");
            }

            var allProperties = ifaceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // FAZ 4b: Indexer'lar (this[int i]) reflection'da GetIndexParameters().Length > 0
            // olan PropertyInfo'lardır. AddProperty mekanizmamız SADECE parametresiz get/set
            // ifade edebiliyor - indexer'ın get_Item(index)/set_Item(index,value) imzasını
            // temsil edemez. Bu yüzden indexer'lar METOT yolundan (AddMethod) gidiyor - zaten
            // rastgele parametre listesini destekliyor, tam da ihtiyacımız olan bu.
            // NOT (bilinen kısıt): AŞIRI YÜKLENMİŞ indexer'lar (this[int] VE this[string] gibi)
            // desteklenmiyor - ikisi de "get_Item" adını paylaşır, DynamicClass.AddMethod bunu
            // isim çakışması olarak (farklı imza, aynı isim) reddeder.
            var properties = allProperties.Where(p => p.GetIndexParameters().Length == 0).ToArray();
            var indexerProperties = allProperties.Where(p => p.GetIndexParameters().Length > 0).ToArray();

            var allMethods = ifaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName) // get_X/set_X/add_X/remove_X'i (özel accessor'lar) hariç tut
                .ToList();

            // Generic metotlar (ör. `T Get<T>()`) AYRI bir yoldan gidiyor - Func<>/Action<>
            // veya DynamicDelegateTypeFactory bunları ifade EDEMEZ (bir metodun kendi açık
            // generic parametresi, tip olarak başka bir metoda taşınamaz). Bkz. FAZ 3b.
            var methods = allMethods.Where(m => !m.IsGenericMethodDefinition).ToList();
            var genericMethods = allMethods.Where(m => m.IsGenericMethodDefinition).ToList();

            if (properties.Length == 0 && indexerProperties.Length == 0 && allMethods.Count == 0)
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

            foreach (var m in genericMethods)
            {
                dc.AddGenericMethod(m);
            }

            var indexerAccessorMethods = new List<MethodInfo>();
            foreach (var p in indexerProperties)
            {
                var getter = p.GetGetMethod();
                if (getter != null)
                {
                    DelegateTypeResolver.AddMethodDynamic(dc, getter.Name, DelegateTypeResolver.Resolve(getter));
                    indexerAccessorMethods.Add(getter);
                }

                var setter = p.GetSetMethod();
                if (setter != null)
                {
                    DelegateTypeResolver.AddMethodDynamic(dc, setter.Name, DelegateTypeResolver.Resolve(setter));
                    indexerAccessorMethods.Add(setter);
                }
            }

            // FAZ 4c: event'ler (ör. `event EventHandler Changed;`). add_X/remove_X metotları
            // zaten yukarıdaki `!IsSpecialName` filtresiyle "methods" listesinden HARİÇ tutuldu -
            // burada AYRI olarak, gerçek bir CLR event'i olarak ekleniyor.
            var events = ifaceType.GetEvents(BindingFlags.Public | BindingFlags.Instance);
            foreach (var e in events)
            {
                dc.AddEvent(e.Name, e.EventHandlerType!);
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

                foreach (var m in genericMethods)
                {
                    if (!members.GenericMethods.TryGetValue(m.Name, out var forwarder))
                    {
                        throw new InvalidOperationException($"[Extend] '{m.Name}' (generic) metodu için üretilen forwarder bulunamadı.");
                    }

                    typeBuilder.DefineMethodOverride(forwarder.Method, m);
                }

                foreach (var m in indexerAccessorMethods)
                {
                    if (!members.Methods.TryGetValue(m.Name, out var forwarder))
                    {
                        throw new InvalidOperationException($"[Extend] '{m.Name}' (indexer) için üretilen forwarder bulunamadı.");
                    }

                    typeBuilder.DefineMethodOverride(forwarder.Method, m);
                }

                foreach (var e in events)
                {
                    if (!members.Events.TryGetValue(e.Name, out var eventForwarder))
                    {
                        throw new InvalidOperationException($"[Extend] '{e.Name}' event'i için üretilen forwarder bulunamadı.");
                    }

                    var ifaceAdd = e.GetAddMethod();
                    if (ifaceAdd != null) typeBuilder.DefineMethodOverride(eventForwarder.Add, ifaceAdd);

                    var ifaceRemove = e.GetRemoveMethod();
                    if (ifaceRemove != null) typeBuilder.DefineMethodOverride(eventForwarder.Remove, ifaceRemove);
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

            // Func<>/Action<> `ref`/`out` parametreleri (byref tipler) VEYA 16'dan fazla
            // parametreyi ifade EDEMEZ - "Type must not be ByRef" hatasıyla çöker (deneyerek
            // bulduk). Böyle durumlarda runtime'da özel bir delegate tipi sentezliyoruz.
            if (paramTypes.Any(t => t.IsByRef) || paramTypes.Length > 16)
            {
                return DynamicDelegateTypeFactory.GetOrCreate(paramTypes, returnType);
            }

            if (returnType == typeof(void))
            {
                return paramTypes.Length == 0 ? typeof(Action) : Expression.GetActionType(paramTypes);
            }

            Type[] funcTypeArgs = paramTypes.Concat(new[] { returnType }).ToArray();
            return Expression.GetFuncType(funcTypeArgs);
        }

        public static void AddMethodDynamic(DynamicClass dc, string methodName, Type delegateType)
        {
            // Artık DynamicClass.AddMethod(string,Type) non-generic overload'ı sayesinde
            // reflection/MakeGenericMethod hack'ine gerek yok - doğrudan çağırıyoruz. Bu aynı
            // zamanda `ref`/`out` içeren (runtime'da sentezlenmiş) delegate tipleri için de
            // ÇALIŞIR - eskiden generic AddMethod<TDelegate>'i MakeGenericMethod ile
            // somutlaştırıyorduk, bu byref bir tip için zaten mümkün DEĞİLDİ (generic type
            // argument olarak byref tip verilemez).
            dc.AddMethod(methodName, delegateType);
        }
    }
}