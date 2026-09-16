using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace DSO.Core.Evoker
{
    /// <summary>
    /// System.Func&lt;...&gt;/System.Action&lt;...&gt; her imzayı ifade edemez: `ref`/`out`
    /// parametreli bir metot için (`bool TryParse(string s, out int r)` gibi) generic Func/Action
    /// tanımına bu tipleri type argument olarak veremezsiniz - CLR "Type must not be ByRef" der
    /// (deneyerek bulduk). Aynı şekilde 16'dan fazla parametreli bir metot için de yerleşik
    /// Func/Action tükenir. Bu sınıf, böyle durumlarda GERÇEK, isimsiz bir delegate TİPİ üretir
    /// (TypeBuilder ile - bir delegate tipi normal bir sınıf gibi ctor(object,IntPtr) + Invoke
    /// metoduyla tanımlanır, gövdesi CLR tarafından özel olarak ele alınır).
    ///
    /// Üretilen tipler İMZAYA göre cache'lenir - aynı (paramTypes, returnType) kombinasyonu
    /// tekrar istenirse aynı delegate Type'ı döner, yeniden emisyon yapılmaz.
    /// </summary>
    public static class DynamicDelegateTypeFactory
    {
        private static readonly object ModuleLock = new();
        private static ModuleBuilder? _module;
        private static int _counter;
        private static readonly ConcurrentDictionary<string, Type> Cache = new();

        /// <summary>
        /// Verilen parametre tipleri + dönüş tipiyle TAM eşleşen bir delegate Type'ı döner.
        /// `ref`/`out` parametreler dahil HERHANGİ bir imza için çalışır (Func/Action'ın
        /// aksine). Aynı imza tekrar istenirse aynı Type'ı döner (cache'li).
        /// </summary>
        public static Type GetOrCreate(Type[] paramTypes, Type returnType)
        {
            string key = BuildKey(paramTypes, returnType);
            return Cache.GetOrAdd(key, _ => Emit(paramTypes, returnType));
        }

        private static string BuildKey(Type[] paramTypes, Type returnType)
        {
            return returnType.AssemblyQualifiedName + "(" +
                string.Join(",", paramTypes.Select(t => t.AssemblyQualifiedName)) + ")";
        }

        private static ModuleBuilder GetModule()
        {
            if (_module != null) return _module;
            lock (ModuleLock)
            {
                if (_module != null) return _module;
                var asmName = new AssemblyName("DSO.Core.Evoker.DynamicDelegates");
                var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
                _module = asmBuilder.DefineDynamicModule("MainModule");
                return _module;
            }
        }

        private static Type Emit(Type[] paramTypes, Type returnType)
        {
            lock (ModuleLock)
            {
                var module = GetModule();
                int id = System.Threading.Interlocked.Increment(ref _counter);
                var typeBuilder = module.DefineType(
                    $"DynamicDelegate_{id}",
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                    typeof(MulticastDelegate));

                // Bir delegate tipinin ctor(object, IntPtr) ve Invoke metodu GÖVDESİZDİR -
                // CLR bunları özel olarak (Runtime) uygular, IL yazmıyoruz, sadece imzayı
                // tanımlayıp ImplementationFlags = Runtime|Managed işaretliyoruz. Bu, .NET'te
                // runtime'da delegate tipi üretmenin standart, belgelenmiş yoludur.
                var ctor = typeBuilder.DefineConstructor(
                    MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
                    CallingConventions.Standard,
                    new[] { typeof(object), typeof(IntPtr) });
                ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

                var invoke = typeBuilder.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                    returnType,
                    paramTypes);
                invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

                return typeBuilder.CreateType()!;
            }
        }
    }
}