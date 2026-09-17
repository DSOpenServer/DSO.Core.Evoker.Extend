using DSO.Core.Evoker.Extend;
using System.Collections.Concurrent;
using System.Reflection;

namespace DSO.Core.Evoker.Extend.TestApi
{
    public class TestClass1
    {
        public string Run(int data, string str)
        {
            return $"Test Class Run edildi.{data.ToString()} - {str}";
        }
        public int Run2() { return 9; }
    }

    // Faz 1'in hedeflediği türden, tamamen property-only interface'ler.
    public interface ICustomer
    {
        int Id { get; set; }
        string Name { get; set; }
    }

    public interface IAuditable
    {
        DateTime CreatedAt { get; set; }
    }

    // Bilerek gerçek bir METOT içeren interface - Faz 1'in REDDETMESİ gereken senaryo.
    public interface IValidatable
    {
        bool Validate();
    }

    // Get-only ve set-only property'ler - Faz 1'in "interface implementasyonu ile somut tip
    // arasında YETKİ FARKI olabilir" ilkesini sınamak için.
    public interface IReadOnlyId
    {
        int Id { get; } // sadece getter - interface Id'yi DIŞARIYA salt-okunur gösteriyor
    }

    public interface IWriteOnlyToken
    {
        string Token { set; } // sadece setter - nadir ama geçerli bir C# kalıbı
    }

    // ==================== FAZ 2: base class senaryoları ====================

    public abstract class Animal
    {
        public abstract string Name { get; set; }
        public abstract int Age { get; set; }

        // SOMUT (abstract olmayan) bir metot - CLR'ın normal inheritance'ı ile
        // OTOMATİK miras alınmalı, biz hiç dokunmuyoruz.
        public string Describe() => $"{Name}, {Age} yaşında";
    }

    public sealed class SealedAnimal
    {
        public int X { get; set; }
    }

    public abstract class NoParameterlessCtorBase
    {
        protected NoParameterlessCtorBase(int required) { }
        public abstract int Value { get; set; }
    }

    public abstract class BaseWithAbstractMethod
    {
        public abstract bool DoSomething(); // gerçek davranışlı - Faz 2 KAPSAMI DIŞI
    }

    public abstract class ProtectedCtorBase
    {
        protected ProtectedCtorBase() { }
        public abstract string Label { get; set; }
    }

    public static class ExtendTests
    {
        public static void RunAll()
        {
            int failures = 0;
            void Check(string name, bool condition, string detail = "")
            {
                if (condition) Console.WriteLine($"  [OK]   {name}");
                else { Console.WriteLine($"  [FAIL] {name}  {detail}"); failures++; }
            }

            Console.WriteLine("=== TEST X1: Implement<T>() ile property'ler OTOMATİK ekleniyor mu (AddProperty demeden) ===");
            {
                var dc = DynamicClass.CreateClass("AutoPropCustomer")
                    .Implement<ICustomer>(); // hiç AddProperty çağrılmadı!

                dc.SetValue<int>("Id", 5);
                dc.SetValue<string>("Name", "Otomatik");

                Check("Id property'si otomatik eklenmiş ve çalışıyor", dc.GetValue<int>("Id") == 5);
                Check("Name property'si otomatik eklenmiş ve çalışıyor", dc.GetValue<string>("Name") == "Otomatik");
                Check("Schema 2 property gösteriyor", dc.Schema.Count == 2, $"count={dc.Schema.Count}");
            }

            Console.WriteLine("=== TEST X2: As<T>() ile interface üzerinden erişim ===");
            {
                var dc = DynamicClass.CreateClass("AsTestCustomer").Implement<ICustomer>();
                dc.SetValue<int>("Id", 10);
                dc.SetValue<string>("Name", "Test");

                ICustomer typed = dc.As<ICustomer>();
                Check("As<T>() doğru instance'ı döndürdü", typed.Id == 10 && typed.Name == "Test", $"Id={typed.Id}, Name={typed.Name}");

                typed.Id = 99; // interface üzerinden SET et
                Check("Interface üzerinden set edilen değer DynamicClass'tan da görünüyor", dc.GetValue<int>("Id") == 99, $"got={dc.GetValue<int>("Id")}");
            }

            Console.WriteLine("=== TEST X3: As<T>() implement edilmemiş bir interface için hata vermeli ===");
            {
                var dc = DynamicClass.CreateClass("NoImplementTest").AddProperty<int>("X");
                dc.SetValue("X", 1);

                bool threw = false;
                try { dc.As<ICustomer>(); }
                catch (InvalidCastException) { threw = true; }

                Check("Implement edilmemiş interface için As<T>() InvalidCastException fırlattı", threw);
            }

            Console.WriteLine("=== TEST X4: Elle AddProperty + Implement<T>() birlikte (idempotent olmalı) ===");
            {
                var dc = DynamicClass.CreateClass("ManualThenImplement")
                    .AddProperty<int>("Id")   // elle, interface'ten ÖNCE
                    .Implement<ICustomer>();  // aynı Id'yi tekrar ekliyor (int, aynı tip) - hata vermemeli

                dc.SetValue<int>("Id", 7);
                Check("Elle eklenen + Implement ile tekrar eklenen aynı-tip property çakışmadı", dc.GetValue<int>("Id") == 7);
            }

            Console.WriteLine("=== TEST X5: Çakışan tipte elle AddProperty + Implement<T>() HATA vermeli ===");
            {
                bool threw = false;
                try
                {
                    DynamicClass.CreateClass("ConflictTest")
                        .AddProperty<string>("Id")  // YANLIŞ tip - ICustomer.Id int bekliyor
                        .Implement<ICustomer>();
                }
                catch (InvalidOperationException) { threw = true; }

                Check("Çakışan tipte AddProperty + Implement<T>() InvalidOperationException fırlattı", threw);
            }

            Console.WriteLine("=== TEST X6: BİRDEN FAZLA interface implement etmek (zincirleme) ===");
            {
                var dc = DynamicClass.CreateClass("MultiInterface")
                    .Implement<ICustomer>()
                    .Implement<IAuditable>();

                dc.SetValue<int>("Id", 1);
                dc.SetValue<string>("Name", "Çoklu");
                var createdAt = DateTime.UtcNow;
                dc.SetValue<DateTime>("CreatedAt", createdAt);

                var asCustomer = dc.As<ICustomer>();
                var asAuditable = dc.As<IAuditable>();

                Check("İki interface de AYNI instance üzerinde çalışıyor", ReferenceEquals(asCustomer, asAuditable));
                Check("ICustomer verisi doğru", asCustomer.Id == 1 && asCustomer.Name == "Çoklu");
                Check("IAuditable verisi doğru", asAuditable.CreatedAt == createdAt, $"got={asAuditable.CreatedAt}");
                Check("Schema 3 property gösteriyor (Id, Name, CreatedAt)", dc.Schema.Count == 3, $"count={dc.Schema.Count}");
            }

            Console.WriteLine("=== TEST X7: FAZ 3 - Gerçek metotlu interface artık DESTEKLENİYOR (SetMethod ile) ===");
            {
                var dc = DynamicClass.CreateClass("ValidatableTest").Implement<IValidatable>();
                dc.SetMethod<Func<bool>>("Validate", () => true);

                var typed = dc.As<IValidatable>();
                Check("Interface üzerinden çağrılan metot doğru sonuç döndü", typed.Validate() == true);

                // dc'nin KENDİ API'si üzerinden de (cast etmeden) çağrılabiliyor mu?
                bool viaInvoke = dc.InvokeMethod<bool>("Validate");
                Check("dc.InvokeMethod<bool>() ile de (cast etmeden) çağrılabiliyor", viaInvoke == true);
            }

            Console.WriteLine("=== TEST X7b: SetMethod ÇAĞRILMADAN metot çağrılırsa NET bir hata almalıyız ===");
            {
                var dc = DynamicClass.CreateClass("UnassignedMethodTest").Implement<IValidatable>();
                // SetMethod HİÇ çağrılmadı

                bool threw = false;
                string? message = null;
                try { dc.InvokeMethod<bool>("Validate"); }
                catch (Exception ex)
                {
                    threw = true;
                    message = ex.InnerException?.Message ?? ex.Message; // EvokerBuilder reflection üzerinden çağırdığı için asıl hata InnerException'da olabilir
                }
                Check("Atanmamış metot çağrısı NET bir hata verdi (sessiz NullReferenceException DEĞİL)", threw, $"message={message}");
            }

            Console.WriteLine("=== TEST X8: Interface değil, sıradan bir class verilirse hata ===");
            {
                bool threw = false;
                try
                {
                    // TestClass1 core'daki gerçek bir sınıf, interface DEĞİL.
                    DynamicClass.CreateClass("NotAnInterfaceTest").Implement<TestApi.TestClass1>();
                }
                catch (ArgumentException) { threw = true; }

                Check("Interface olmayan bir tip Implement<T>() ile reddedildi", threw);
            }

            Console.WriteLine("=== TEST X9: Extend, core'a GERÇEKTEN ayrı bir derlenmiş DLL üzerinden mi bağlı? ===");
            {
                var asm = typeof(DynamicClassInterfaceExtensions).Assembly;
                Check("DynamicClassInterfaceExtensions farklı bir assembly'de yaşıyor",
                    asm != typeof(DynamicClass).Assembly,
                    $"Extend assembly={asm.GetName().Name}, Core assembly={typeof(DynamicClass).Assembly.GetName().Name}");
                Console.WriteLine($"  Core assembly : {typeof(DynamicClass).Assembly.GetName().Name}");
                Console.WriteLine($"  Extend assembly: {asm.GetName().Name}");
            }

            Console.WriteLine("=== TEST X9: Extend, core'a GERÇEKTEN ayrı bir derlenmiş DLL üzerinden mi bağlı? ===");
            {
                var asm = typeof(DynamicClassInterfaceExtensions).Assembly;
                Check("DynamicClassInterfaceExtensions farklı bir assembly'de yaşıyor",
                    asm != typeof(DynamicClass).Assembly,
                    $"Extend assembly={asm.GetName().Name}, Core assembly={typeof(DynamicClass).Assembly.GetName().Name}");
                Console.WriteLine($"  Core assembly : {typeof(DynamicClass).Assembly.GetName().Name}");
                Console.WriteLine($"  Extend assembly: {asm.GetName().Name}");
            }

            Console.WriteLine("=== TEST X10: GET-ONLY interface property - somut tip YİNE DE settable kalmalı ===");
            {
                var dc = DynamicClass.CreateClass("ReadOnlyIdTest").Implement<IReadOnlyId>();
                dc.SetValue<int>("Id", 77); // interface bunu göstermiyor ama DynamicClass hâlâ yazabiliyor olmalı

                var typed = dc.As<IReadOnlyId>();
                Check("Interface üzerinden (salt-okunur) doğru değer okunuyor", typed.Id == 77, $"got={typed.Id}");

                // Interface tipinde SADECE getter var - C# derleme zamanında typed.Id = X yazamayız zaten (istenen davranış budur).
                // Somut tip üzerinden (DynamicClass.SetValue) hâlâ yazılabilir olduğunu doğruladık (yukarıda).
                Check("DynamicClass üzerinden hâlâ yazılabiliyor (somut tip interface'den daha esnek)",
                    dc.GetValue<int>("Id") == 77);
            }

            Console.WriteLine("=== TEST X11: SET-ONLY interface property ===");
            {
                var dc = DynamicClass.CreateClass("WriteOnlyTokenTest").Implement<IWriteOnlyToken>();

                var typed = dc.As<IWriteOnlyToken>();
                typed.Token = "gizli-token"; // interface üzerinden SADECE yazılabilir

                // Ama somut tip (DynamicClass) üzerinden OKUNABİLİR - çünkü core her zaman hem get hem set üretir.
                Check("Set-only interface property'nin değeri DynamicClass üzerinden OKUNABİLİYOR (somut tip daha esnek)",
                    dc.GetValue<string>("Token") == "gizli-token", $"got={dc.GetValue<string>("Token")}");
            }

            // ==================== FAZ 2: base class testleri ====================

            Console.WriteLine("=== TEST Y1: Extend<TBase>() - abstract property otomatik ekleniyor, SOMUT metot miras alınıyor ===");
            {
                var dc = DynamicClass.CreateClass("AnimalTest").Extend<Animal>();

                dc.SetValue<string>("Name", "Karabaş");
                dc.SetValue<int>("Age", 3);

                Animal typed = (Animal)dc.RawInstance;
                Check("Abstract property'ler (Name, Age) otomatik eklendi ve doğru", typed.Name == "Karabaş" && typed.Age == 3, $"Name={typed.Name}, Age={typed.Age}");

                Check("Base class'ın SOMUT metodu (Describe) miras alındı ve bizim verimizi kullanıyor (cast üzerinden)",
                    typed.Describe() == "Karabaş, 3 yaşında", $"got={typed.Describe()}");

                // DÜZELTME: dc'nin KENDİ API'si üzerinden de (cast etmeden) çağrılabiliyor mu?
                // Önceki sürümde bu test SADECE cast ederek doğruluyordu - dc.InvokeMethod
                // eklenene kadar dc'nin kendi üzerinden metot çağırmanın bir yolu yoktu.
                string viaDc = dc.InvokeMethod<string>("Describe")!;
                Check("dc.InvokeMethod<string>(\"Describe\") ile de (cast etmeden) doğru çalışıyor",
                    viaDc == "Karabaş, 3 yaşında", $"got={viaDc}");

                Check("Üretilen tip gerçekten Animal'dan türüyor", typeof(Animal).IsAssignableFrom(dc.Type));
            }

            Console.WriteLine("=== TEST Y2: Sealed base class REDDEDİLMELİ ===");
            {
                bool threw = false;
                try { DynamicClass.CreateClass("SealedTest").Extend<SealedAnimal>(); }
                catch (NotSupportedException) { threw = true; }
                Check("Sealed sınıf Extend<T>() ile reddedildi", threw);
            }

            Console.WriteLine("=== TEST Y3: Parametresiz ctor'u OLMAYAN base class REDDEDİLMELİ ===");
            {
                bool threw = false;
                try { DynamicClass.CreateClass("NoCtorTest").Extend<NoParameterlessCtorBase>(); }
                catch (NotSupportedException) { threw = true; }
                Check("Parametresiz ctor'u olmayan sınıf reddedildi (net hata, TypeLoadException DEĞİL)", threw);
            }

            Console.WriteLine("=== TEST Y4: FAZ 3 - Abstract METOT içeren base class artık DESTEKLENİYOR (SetMethod ile) ===");
            {
                var dc = DynamicClass.CreateClass("AbstractMethodTest").Extend<BaseWithAbstractMethod>();
                dc.SetMethod<Func<bool>>("DoSomething", () => true);

                var typed = (BaseWithAbstractMethod)dc.RawInstance;
                Check("Abstract metot (base class) SetMethod ile dolduruldu ve cast üzerinden çalışıyor", typed.DoSomething() == true);

                bool viaDc = dc.InvokeMethod<bool>("DoSomething");
                Check("dc.InvokeMethod ile de çalışıyor", viaDc == true);
            }

            Console.WriteLine("=== TEST Y4b: Abstract metot SetMethod'suz çağrılırsa net hata ===");
            {
                var dc = DynamicClass.CreateClass("AbstractMethodUnassignedTest").Extend<BaseWithAbstractMethod>();
                bool threw = false;
                try { dc.InvokeMethod<bool>("DoSomething"); }
                catch (Exception) { threw = true; }
                Check("SetMethod çağrılmadan abstract metot çağrısı hata verdi", threw);
            }

            Console.WriteLine("=== TEST Y5: PROTECTED parametresiz ctor kabul edilmeli ===");
            {
                var dc = DynamicClass.CreateClass("ProtectedCtorTest").Extend<ProtectedCtorBase>();
                dc.SetValue<string>("Label", "test");
                var typed = (ProtectedCtorBase)dc.RawInstance;
                Check("Protected ctor'lu base class kabul edildi ve çalışıyor", typed.Label == "test", $"got={typed.Label}");
            }

            Console.WriteLine("=== TEST Y6: AYNI ANDA hem base class'tan türe hem interface implement et ===");
            {
                var dc = DynamicClass.CreateClass("CombinedTest")
                    .Extend<Animal>()
                    .Implement<IAuditable>();

                dc.SetValue<string>("Name", "Pamuk");
                dc.SetValue<int>("Age", 2);
                var now = DateTime.UtcNow;
                dc.SetValue<DateTime>("CreatedAt", now);

                var asAnimal = (Animal)dc.RawInstance;
                var asAuditable = dc.As<IAuditable>();

                Check("Base class'tan türeme çalışıyor", asAnimal.Describe() == "Pamuk, 2 yaşında", $"got={asAnimal.Describe()}");
                Check("Interface implementasyonu da AYNI ANDA çalışıyor", asAuditable.CreatedAt == now);
                Check("İkisi de aynı instance", ReferenceEquals(asAnimal, asAuditable));
                Check("Şema 3 property (Name, Age, CreatedAt) gösteriyor", dc.Schema.Count == 3, $"count={dc.Schema.Count}");
            }

            // ==================== FAZ 3: saf AddMethod/SetMethod (interface/base OLMADAN) ====================

            Console.WriteLine("=== TEST Z1: DynamicClass.AddMethod/SetMethod - HİÇ interface/base OLMADAN, saf core ===");
            {
                var dc = DynamicClass.CreateClass("PureMethodTest")
                    .AddProperty<int>("Base")
                    .AddMethod<Func<int, int, int>>("Add"); // 2 parametreli, int dönen bir metot

                dc.SetValue<int>("Base", 100);
                dc.SetMethod<Func<int, int, int>>("Add", (a, b) => a + b + dc.GetValue<int>("Base"));

                int result = dc.InvokeMethod<int>("Add", 5, 7);
                Check("2 parametreli, int dönen metot doğru çalıştı (rastgele arity IL emisyonu doğrulandı)",
                    result == 112, $"got={result}"); // 5+7+100
            }

            Console.WriteLine("=== TEST Z2: void (Action) metot ===");
            {
                var dc = DynamicClass.CreateClass("VoidMethodTest")
                    .AddProperty<int>("Counter")
                    .AddMethod<Action<int>>("Increment");

                dc.SetValue<int>("Counter", 0);
                dc.SetMethod<Action<int>>("Increment", by =>
                {
                    dc.SetValue<int>("Counter", dc.GetValue<int>("Counter") + by);
                });

                dc.InvokeMethod("Increment", 5);
                dc.InvokeMethod("Increment", 3);

                Check("Void (Action) metot doğru çalıştı, state güncellendi", dc.GetValue<int>("Counter") == 8, $"got={dc.GetValue<int>("Counter")}");
            }

            Console.WriteLine("=== TEST Z3: Parametresiz Action (Faz 3'ün en basit durumu) ===");
            {
                var dc = DynamicClass.CreateClass("NoArgActionTest").AddMethod<Action>("DoNothing");
                bool called = false;
                dc.SetMethod<Action>("DoNothing", () => called = true);
                dc.InvokeMethod("DoNothing");
                Check("Parametresiz Action doğru çalıştı", called);
            }

            Console.WriteLine("=== TEST Z4: AYNI ŞEMA + FARKLI methods -> şema cache KARIŞTIRMAMALI ===");
            {
                var dc1 = DynamicClass.CreateClass("SameSchemaDifferentMethods").AddProperty<int>("X").AddMethod<Func<int>>("GetX");
                var dc2 = DynamicClass.CreateClass("SameSchemaDifferentMethods").AddProperty<int>("X").AddMethod<Action>("Reset");

                dc1.SetValue<int>("X", 1);
                dc2.SetValue<int>("X", 2);

                Check("Aynı properties ama FARKLI methods -> FARKLI Type üretildi (cache key methods'u da içeriyor)",
                    !ReferenceEquals(dc1.Type, dc2.Type));
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "TÜM EXTEND (FAZ 1+2+3) TESTLERİ GEÇTİ ✅" : $"{failures} TEST BAŞARISIZ ❌");
        }
    }

    public delegate bool TryParseDelegate(string input, out int result);
    public delegate void AddByRefDelegate(ref int value, int amount);

    public interface IParser
    {
        bool TryParse(string input, out int result);
    }

    public static class RefOutTests
    {
        // Delegate.CreateDelegate ile bağlanacak GERÇEK metotlar (lambda DEĞİL - runtime'da
        // sentezlenmiş bir delegate tipine lambda yazamayız, ama gerçek bir metot grubu
        // Delegate.CreateDelegate ile HERHANGİ bir uyumlu delegate tipine bağlanabilir).
        private static bool MyTryParse(string input, out int result) => int.TryParse(input, out result);

        public static void RunAll()
        {
            int failures = 0;
            void Check(string name, bool condition, string detail = "")
            {
                if (condition) Console.WriteLine($"  [OK]   {name}");
                else { Console.WriteLine($"  [FAIL] {name}  {detail}"); failures++; }
            }

            Console.WriteLine("=== TEST R1: Standalone AddMethod/SetMethod + 'out' + düz reflection çağrısı ===");
            {
                var dc = DynamicClass.CreateClass("StandaloneOutTest").AddMethod<TryParseDelegate>("TryParse");
                dc.SetMethod<TryParseDelegate>("TryParse", (string input, out int result) => int.TryParse(input, out result));

                var method = dc.Type.GetMethod("TryParse")!;
                object?[] args = { "123", null };
                object? ret = method.Invoke(dc.RawInstance, args);

                Check("'out' değeri düz reflection ile doğru geldi", ret is true && (int)args[1]! == 123, $"ret={ret}, out={args[1]}");
            }

            Console.WriteLine("=== TEST R2: Standalone AddMethod/SetMethod + 'ref' + düz reflection çağrısı ===");
            {
                var dc = DynamicClass.CreateClass("StandaloneRefTest").AddMethod<AddByRefDelegate>("AddByRef");
                dc.SetMethod<AddByRefDelegate>("AddByRef", (ref int value, int amount) => value += amount);

                var method = dc.Type.GetMethod("AddByRef")!;
                object?[] args = { 10, 5 };
                method.Invoke(dc.RawInstance, args);

                Check("'ref' değeri düz reflection ile doğru güncellendi", (int)args[0]! == 15, $"got={args[0]}");
            }

            Console.WriteLine("=== TEST R3: dc.InvokeMethod artık ref/out için SESSİZCE YANLIŞ SONUÇ VERMİYOR, AÇIKÇA REDDEDİYOR ===");
            {
                var dc = DynamicClass.CreateClass("InvokeMethodGuardTest").AddMethod<TryParseDelegate>("TryParse");
                dc.SetMethod<TryParseDelegate>("TryParse", (string input, out int result) => int.TryParse(input, out result));

                bool threw = false;
                string? message = null;
                try { dc.InvokeMethod<bool>("TryParse", "42", 0); }
                catch (NotSupportedException ex) { threw = true; message = ex.Message; }

                Check("dc.InvokeMethod artık NotSupportedException fırlatıyor (sessiz veri kaybı YOK)", threw, $"message={message}");
            }

            Console.WriteLine("=== TEST R4: Implement<IParser>() - 'out' İÇEREN interface artık ÇÖKMÜYOR ===");
            {
                var dc = DynamicClass.CreateClass("ParserImplTest").Implement<IParser>();
                Check("Implement<IParser>() ArgumentException fırlatmadan tamamlandı", true);

                // Auto-derive edilen delegate tipini alıp GERÇEK bir metotla (lambda değil!) bağlıyoruz.
                Type delegateType = dc.GetMethodDelegateType("TryParse");
                Delegate impl = Delegate.CreateDelegate(delegateType, typeof(RefOutTests).GetMethod(nameof(MyTryParse), BindingFlags.NonPublic | BindingFlags.Static)!);
                dc.SetMethod("TryParse", impl);

                // Interface üzerinden (normal C# virtual call ile) çağıralım - out değeri BURADA sorunsuz gelir.
                IParser typed = dc.As<IParser>();
                bool ok = typed.TryParse("777", out int result);

                Check("Interface üzerinden 'out' değeri doğru geldi", ok && result == 777, $"ok={ok}, result={result}");
            }

            Console.WriteLine("=== TEST R5: 'ref' parametreli metot GetMethodDelegateType'ta doğru tip mi döndürüyor ===");
            {
                var dc = DynamicClass.CreateClass("RefTypeCheckTest").AddMethod<AddByRefDelegate>("AddByRef");
                Type dt = dc.GetMethodDelegateType("AddByRef");
                Check("GetMethodDelegateType doğru tipi döndürdü", dt == typeof(AddByRefDelegate), $"got={dt.Name}");
            }

            Console.WriteLine("=== TEST R6: SetMethod(string,Delegate) tip uyuşmazlığında hata veriyor mu ===");
            {
                var dc = DynamicClass.CreateClass("MismatchTest").AddMethod<Action>("DoIt");
                bool threw = false;
                try { dc.SetMethod("DoIt", (Action<int>)(x => { })); } // yanlış delegate tipi
                catch (ArgumentException) { threw = true; }
                Check("Yanlış delegate tipi ArgumentException fırlattı", threw);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "TÜM ref/out TESTLERİ GEÇTİ ✅" : $"{failures} TEST BAŞARISIZ ❌");
        }
    }

    public interface IGenericContainer
    {
        T Echo<T>(T value);
        void Log<T>(T value);
        TResult Convert<TInput, TResult>(TInput input);
        bool TryGet<T>(string key, out T value);        // generic + out
        void Increment<T>(ref T value, T amount) where T : struct; // generic + ref
    }

    public abstract class GenericBase
    {
        public abstract T GetDefault<T>();
        public string NonGenericConcrete() => "somut metot, dokunulmadı";
    }

    public static class GenericMethodTests
    {
        public static void RunAll()
        {
            int failures = 0;
            void Check(string name, bool condition, string detail = "")
            {
                if (condition) Console.WriteLine($"  [OK]   {name}");
                else { Console.WriteLine($"  [FAIL] {name}  {detail}"); failures++; }
            }

            Console.WriteLine("=== TEST G1: Standalone AddGenericMethod - HİÇ interface OLMADAN ===");
            {
                var templateMethod = typeof(IGenericContainer).GetMethod(nameof(IGenericContainer.Echo))!;
                // NOT: standalone kullanımda da bir 'şekil' vermek için var olan bir MethodInfo
                // (burada bir interface'in metodu) template olarak veriliyor - core'un kendisi
                // interface implementasyonu YAPMIYOR, sadece imzayı kopyalıyor.
                var dc = DynamicClass.CreateClass("StandaloneGenericTest").AddGenericMethod(templateMethod);

                dc.SetGenericMethod("Echo", (typeArgs, args) =>
                {
                    Console.WriteLine($"    [delegate] T={typeArgs[0].Name}, value={args[0]}");
                    return args[0];
                });

                var method = dc.Type.GetMethod("Echo")!;
                var intEcho = method.MakeGenericMethod(typeof(int));
                object? intResult = intEcho.Invoke(dc.RawInstance, new object[] { 42 });

                var stringEcho = method.MakeGenericMethod(typeof(string));
                object? stringResult = stringEcho.Invoke(dc.RawInstance, new object[] { "merhaba" });

                Check("Echo<int>(42) doğru çalıştı", intResult is int i && i == 42, $"got={intResult}");
                Check("Echo<string>(\"merhaba\") doğru çalıştı", stringResult is string s && s == "merhaba", $"got={stringResult}");
            }

            Console.WriteLine("=== TEST G2: Implement<IGenericContainer>() - interface'ten OTOMATİK, birden fazla generic metot ===");
            {
                var dc = DynamicClass.CreateClass("ImplGenericTest").Implement<IGenericContainer>();

                dc.SetGenericMethod("Echo", (typeArgs, args) => args[0]);
                dc.SetGenericMethod("Log", (typeArgs, args) => { Console.WriteLine($"    [Log<{typeArgs[0].Name}>] {args[0]}"); return null; });
                dc.SetGenericMethod("Convert", (typeArgs, args) =>
                {
                    // Convert<TInput,TResult>(TInput input) - basit bir örnek: ToString() + parse
                    if (typeArgs[1] == typeof(string)) return args[0]?.ToString();
                    return System.Convert.ChangeType(args[0], typeArgs[1]);
                });

                IGenericContainer typed = dc.As<IGenericContainer>();

                Check("Interface üzerinden Echo<int> çalışıyor", typed.Echo(99) == 99);
                Check("Interface üzerinden Echo<string> çalışıyor", typed.Echo("test") == "test");

                typed.Log(123); // sadece çökmemeli

                string converted = typed.Convert<int, string>(456);
                Check("Interface üzerinden Convert<int,string> çalışıyor", converted == "456", $"got={converted}");
            }

            Console.WriteLine("=== TEST G3: Extend<GenericBase>() - abstract generic metot + SOMUT metot miras alımı ===");
            {
                var dc = DynamicClass.CreateClass("ExtendGenericTest").Extend<GenericBase>();
                dc.SetGenericMethod("GetDefault", (typeArgs, args) =>
                {
                    Type t = typeArgs[0];
                    return t.IsValueType ? Activator.CreateInstance(t) : null;
                });

                var typed = (GenericBase)dc.RawInstance;
                Check("Abstract generic metot (GetDefault<int>) çalışıyor", typed.GetDefault<int>() == 0);
                Check("Abstract generic metot (GetDefault<string>) çalışıyor", typed.GetDefault<string>() == null);
                Check("Base class'ın SOMUT metodu hâlâ miras alınmış durumda", typed.NonGenericConcrete() == "somut metot, dokunulmadı");
            }

            Console.WriteLine("=== TEST G4: SetGenericMethod ÇAĞRILMADAN çağrılırsa net hata ===");
            {
                var templateMethod = typeof(IGenericContainer).GetMethod(nameof(IGenericContainer.Echo))!;
                var dc = DynamicClass.CreateClass("UnassignedGenericTest").AddGenericMethod(templateMethod);

                var method = dc.Type.GetMethod("Echo")!.MakeGenericMethod(typeof(int));
                bool threw = false;
                try { method.Invoke(dc.RawInstance, new object[] { 1 }); }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
                {
                    threw = true;
                }
                Check("Atanmamış generic metot çağrısı net bir hata verdi", threw);
            }

            Console.WriteLine("=== TEST G5: AYNI ŞEMA + FARKLI generic metot -> şema cache KARIŞTIRMAMALI ===");
            {
                var t1 = typeof(IGenericContainer).GetMethod(nameof(IGenericContainer.Echo))!;
                var t2 = typeof(IGenericContainer).GetMethod(nameof(IGenericContainer.Log))!;

                var dc1 = DynamicClass.CreateClass("SameSchemaGeneric").AddProperty<int>("X").AddGenericMethod(t1);
                var dc2 = DynamicClass.CreateClass("SameSchemaGeneric").AddProperty<int>("X").AddGenericMethod(t2);

                Check("Aynı properties ama FARKLI generic method -> FARKLI Type üretildi", !ReferenceEquals(dc1.Type, dc2.Type));
            }

            Console.WriteLine("=== TEST G6: FAZ 3c - Generic + 'out' kombinasyonu (bool TryGet<T>(string, out T)) ===");
            {
                var dc = DynamicClass.CreateClass("GenericOutTest").Implement<IGenericContainer>();
                var store = new Dictionary<string, object> { ["age"] = 42, ["name"] = "Ahmet" };

                dc.SetGenericMethod("Echo", (typeArgs, args) => args[0]);
                dc.SetGenericMethod("Log", (typeArgs, args) => null);
                dc.SetGenericMethod("Convert", (typeArgs, args) => args[0]?.ToString());
                dc.SetGenericMethod("TryGet", (typeArgs, args) =>
                {
                    // args[0] = key (string, normal), args[1] = holder (object[1]) çünkü 'out T value'
                    // ÖNEMLİ SÖZLEŞME: T bir value type olabileceği için holder[0]'a ASLA çıplak
                    // null koymayın (forwarder Unbox_Any ile açacak, null'da NullReferenceException
                    // alırsınız) - "değer yok" durumunda bile typeArgs[0]'ın GEÇERLİ bir boxlanmış
                    // varsayılan değerini koyun (value type için Activator.CreateInstance, reference
                    // type için null GÜVENLİDİR çünkü Unbox_Any referans tiplerinde düz bir cast'tir).
                    string key = (string)args[0]!;
                    var holder = (object?[])args[1]!;
                    Type t = typeArgs[0];
                    if (store.TryGetValue(key, out var val) && t.IsInstanceOfType(val))
                    {
                        holder[0] = val;
                        return true;
                    }
                    holder[0] = t.IsValueType ? Activator.CreateInstance(t) : null;
                    return false;
                });
                dc.SetGenericMethod("Increment", (typeArgs, args) =>
                {
                    // args[0] = holder (ref T value), args[1] = amount (normal, aynı T)
                    var holder = (object?[])args[0]!;
                    dynamic current = holder[0]!;
                    dynamic amount = args[1]!;
                    holder[0] = current + amount;
                    return null;
                });

                IGenericContainer typed = dc.As<IGenericContainer>();

                bool found = typed.TryGet<int>("age", out int age);
                Check("TryGet<int> ('out' + generic) doğru değeri buldu", found && age == 42, $"found={found}, age={age}");

                bool notFound = typed.TryGet<int>("yok", out int missing);
                Check("TryGet<int> olmayan key için false döndü", !notFound, $"notFound={notFound}");

                int counter = 10;
                typed.Increment(ref counter, 5);
                Check("Increment<int> ('ref' + generic) doğru güncellendi", counter == 15, $"got={counter}");
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "TÜM GENERIC METOT TESTLERİ GEÇTİ ✅" : $"{failures} TEST BAŞARISIZ ❌");
        }
    }

    // ==================== Madde 3: Event ====================
    public interface IObservableThing
    {
        int Value { get; set; }
        event EventHandler Changed;
    }

    // ==================== Madde 4: Indexer ====================
    public interface IBag
    {
        int this[string key] { get; set; }
        int Count { get; }
    }

    // ==================== Madde 14: Attribute enjeksiyonu ====================
    [AttributeUsage(AttributeTargets.Class)]
    public class MyMarkerAttribute : Attribute
    {
        public string Label { get; }
        public MyMarkerAttribute(string label) { Label = label; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class MyFieldNameAttribute : Attribute
    {
        public string Name { get; }
        public MyFieldNameAttribute(string name) { Name = name; }
    }

    public static class NewGapsTests
    {
        public static void RunAll()
        {
            int failures = 0;
            void Check(string name, bool condition, string detail = "")
            {
                if (condition) Console.WriteLine($"  [OK]   {name}");
                else { Console.WriteLine($"  [FAIL] {name}  {detail}"); failures++; }
            }

            Console.WriteLine("=== TEST N1: Standalone AddEvent/RaiseEvent - HİÇ interface OLMADAN ===");
            {
                var dc = DynamicClass.CreateClass("StandaloneEventTest").AddEvent<EventHandler>("Changed");

                int callCount = 0;
                EventHandler? handler = (s, e) => callCount++;

                var addMethod = dc.Type.GetEvent("Changed")!.GetAddMethod()!;
                addMethod.Invoke(dc.RawInstance, new object?[] { handler });

                dc.RaiseEvent("Changed", dc.RawInstance, EventArgs.Empty);
                dc.RaiseEvent("Changed", dc.RawInstance, EventArgs.Empty);

                Check("Event 2 kez tetiklendi", callCount == 2, $"got={callCount}");

                var removeMethod = dc.Type.GetEvent("Changed")!.GetRemoveMethod()!;
                removeMethod.Invoke(dc.RawInstance, new object?[] { handler });
                dc.RaiseEvent("Changed", dc.RawInstance, EventArgs.Empty);
                Check("Remove sonrası tetiklenmedi", callCount == 2, $"got={callCount}");
            }

            Console.WriteLine("=== TEST N2: Implement<IObservableThing>() - interface event + normal C# += syntax ===");
            {
                var dc = DynamicClass.CreateClass("ImplEventTest").Implement<IObservableThing>();
                dc.SetValue<int>("Value", 0);

                IObservableThing typed = dc.As<IObservableThing>();

                int callCount = 0;
                int lastValue = -1;
                typed.Changed += (s, e) => { callCount++; lastValue = ((IObservableThing)s!).Value; };

                dc.SetValue<int>("Value", 42);
                dc.RaiseEvent("Changed", dc.RawInstance, EventArgs.Empty);

                Check("Interface event normal += ile abone olundu ve tetiklendi", callCount == 1, $"got={callCount}");
                Check("Event handler doğru instance/değeri gördü", lastValue == 42, $"got={lastValue}");

                typed.Changed -= (s, e) => { }; // farklı bir lambda -= etkisiz olmalı (normal .NET davranışı)
                dc.RaiseEvent("Changed", dc.RawInstance, EventArgs.Empty);
                Check("Yanlış handler ile -= çıkarma işe yaramadı (hâlâ 2 kez çağrılmalı)", callCount == 2, $"got={callCount}");
            }

            Console.WriteLine("=== TEST N3: Event'siz çağrı (kimse dinlemiyor) sessizce no-op ===");
            {
                var dc = DynamicClass.CreateClass("NoSubscriberEventTest").AddEvent<EventHandler>("Changed");
                bool threw = false;
                try { dc.RaiseEvent("Changed", dc.RawInstance, EventArgs.Empty); }
                catch { threw = true; }
                Check("Abone olunmamış event tetiklemesi hata vermedi", !threw);
            }

            Console.WriteLine("=== TEST N4: Implement<IBag>() - indexer (this[string]) interface üzerinden ===");
            {
                var dc = DynamicClass.CreateClass("BagImplTest").Implement<IBag>();

                // Indexer setter/getter'ı SetMethod ile dolduruyoruz (get_Item/set_Item birer metot).
                // Count İSE index parametresi olmayan NORMAL bir property - AddProperty/SetValue
                // yoluna gider (metot değil), bu yüzden manuel senkronize ediyoruz.
                var store = new Dictionary<string, int>();
                dc.SetMethod<Func<string, int>>("get_Item", key => store.TryGetValue(key, out var v) ? v : 0);
                dc.SetMethod<Action<string, int>>("set_Item", (key, value) => { store[key] = value; dc.SetValue<int>("Count", store.Count); });
                dc.SetValue<int>("Count", 0);

                IBag typed = dc.As<IBag>();
                typed["a"] = 10;
                typed["b"] = 20;

                Check("Indexer üzerinden yazılan değer okunuyor", typed["a"] == 10 && typed["b"] == 20, $"a={typed["a"]}, b={typed["b"]}");
                Check("Count property de çalışıyor (elle senkronize edilen normal property)", typed.Count == 2, $"got={typed.Count}");
            }

            Console.WriteLine("=== TEST N5: AddTypeAttribute / AddPropertyAttribute ===");
            {
                var dc = DynamicClass.CreateClass("AttributeTest")
                    .AddProperty<int>("Id");
                dc.AddTypeAttribute<MyMarkerAttribute>("test-etiketi");
                dc.AddPropertyAttribute<MyFieldNameAttribute>("Id", "custom_id");
                dc.SetValue<int>("Id", 1); // build tetikler

                var typeAttr = dc.Type.GetCustomAttribute<MyMarkerAttribute>();
                Check("Tip attribute'u uygulanmış", typeAttr != null && typeAttr.Label == "test-etiketi", $"got={typeAttr?.Label}");

                var propAttr = dc.Type.GetProperty("Id")!.GetCustomAttribute<MyFieldNameAttribute>();
                Check("Property attribute'u uygulanmış", propAttr != null && propAttr.Name == "custom_id", $"got={propAttr?.Name}");
            }

            Console.WriteLine("=== TEST N6: Warmup - fonksiyonel olarak çalışıyor mu (hata vermeden) ===");
            {
                var dc = DynamicClass.CreateClass("WarmupTest")
                    .AddProperty<int>("Id")
                    .AddProperty<string>("Name")
                    .AddMethod<Func<int, int>>("Double");
                dc.SetMethod<Func<int, int>>("Double", x => x * 2);
                dc.SetValue<int>("Id", 1);
                dc.SetValue<string>("Name", "test");

                dc.Warmup(); // hata vermemeli

                var sw = System.Diagnostics.Stopwatch.StartNew();
                int result = dc.InvokeMethod<int>("Double", 21);
                sw.Stop();

                Check("Warmup sonrası metot doğru çalışıyor", result == 42, $"got={result}");
                Check("Warmup sonrası property'ler hâlâ doğru", dc.GetValue<int>("Id") == 1 && dc.GetValue<string>("Name") == "test");
            }

            Console.WriteLine("=== TEST N7: İç içe DynamicClass property'si (bir dinamik tipin property'si başka bir dinamik tip) ===");
            {
                var address = DynamicClass.CreateClass("AddressType").AddProperty<string>("City");
                address.SetValue<string>("City", "İstanbul");

                var person = DynamicClass.CreateClass("PersonType")
                    .AddProperty<string>("Name")
                    .AddProperty("Address", address.Type); // İÇ İÇE - property tipi başka bir DynamicClass'ın ürettiği Type

                person.SetValue<string>("Name", "Ahmet");
                person.SetValue("Address", address.RawInstance); // object olarak set (nested instance)

                Check("Person.Name doğru", person.GetValue<string>("Name") == "Ahmet");

                object nestedAddress = person.GetValue("Address")!;
                var cityGetter = DynamicEntityAccessor.GetGetter<string>(address.Type, "City");
                Check("İç içe Address.City doğru okunuyor", cityGetter(nestedAddress) == "İstanbul", $"got={cityGetter(nestedAddress)}");
            }

            Console.WriteLine("=== TEST N8: Koleksiyon tipli property'ler (List<int>, int[], Dictionary<string,int>) ===");
            {
                var dc = DynamicClass.CreateClass("CollectionPropsTest")
                    .AddProperty<List<int>>("Numbers")
                    .AddProperty<int[]>("Tags")
                    .AddProperty<Dictionary<string, int>>("Scores");

                dc.SetValue("Numbers", new List<int> { 1, 2, 3 });
                dc.SetValue("Tags", new[] { 10, 20 });
                dc.SetValue("Scores", new Dictionary<string, int> { ["a"] = 1 });

                var numbers = dc.GetValue<List<int>>("Numbers");
                var tags = dc.GetValue<int[]>("Tags");
                var scores = dc.GetValue<Dictionary<string, int>>("Scores");

                Check("List<int> property doğru", numbers.Count == 3 && numbers[2] == 3, $"count={numbers.Count}");
                Check("int[] property doğru", tags.Length == 2 && tags[1] == 20, $"len={tags.Length}");
                Check("Dictionary<string,int> property doğru", scores["a"] == 1, $"got={scores.GetValueOrDefault("a")}");
            }

            Console.WriteLine("=== TEST N9: Thread-safety - AYNI DynamicClass'ı ÇOK THREAD'DEN EŞ ZAMANLI build etmek ===");
            {
                var dc = DynamicClass.CreateClass("ThreadSafetyBuildTest").AddProperty<int>("X");

                var types = new ConcurrentBag<Type>();
                var instances = new ConcurrentBag<object>();

                Parallel.For(0, 50, i =>
                {
                    // Type/RawInstance property'leri EnsureBuilt() tetikler - 50 thread AYNI ANDA
                    // ilk build'i tetiklemeye çalışıyor.
                    types.Add(dc.Type);
                    instances.Add(dc.RawInstance);
                });

                Check("50 thread'in hepsi AYNI Type'ı gördü (Type SADECE BİR KEZ üretildi)",
                    types.Distinct().Count() == 1, $"distinct count={types.Distinct().Count()}");
                Check("50 thread'in hepsi AYNI instance'ı gördü", instances.Distinct().Count() == 1);
            }

            Console.WriteLine("=== TEST N10: Thread-safety - eş zamanlı OnSet hook ekleme + SetValue çağrısı çakışmamalı (crash yok) ===");
            {
                var dc = DynamicClass.CreateClass("ThreadSafetyHookTest").AddProperty<int>("Counter");
                dc.SetValue<int>("Counter", 0);

                bool crashed = false;
                try
                {
                    Parallel.Invoke(
                        () => { for (int i = 0; i < 200; i++) dc.OnSet<int>("Counter", (o, n) => { }); },
                        () => { for (int i = 0; i < 200; i++) dc.SetValue<int>("Counter", i); },
                        () => { for (int i = 0; i < 200; i++) dc.GetValue<int>("Counter"); }
                    );
                }
                catch (Exception ex)
                {
                    crashed = true;
                    Console.WriteLine($"    Hata: {ex.GetType().Name}: {ex.Message}");
                }

                Check("Eş zamanlı hook ekleme + SetValue/GetValue çökmedi", !crashed);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "TÜM YENİ AÇIK MADDE TESTLERİ GEÇTİ ✅" : $"{failures} TEST BAŞARISIZ ❌");
        }
    }
}