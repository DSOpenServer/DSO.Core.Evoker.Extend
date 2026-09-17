# DSO.Core.Evoker.Extend

**Runtime'da ürettiğiniz tipler, gerçek interface'leri ve base class'ları — sadece veri değil, davranışıyla birlikte — implement etsin.**

`DSO.Core.Evoker.Extend`, [`DSO.Core.Evoker`](../DSO.Core.Evoker/README.md) üzerine kurulu, onu hiç değiştirmeden çalışan opsiyonel bir katmandır. Çekirdek kütüphane size property'lerden oluşan runtime tipleri verir; bu paket ise o tiplerin **var olan bir interface'i implement etmesini**, **var olan bir sınıftan türemesini**, ve isterseniz **gerçek davranışlı metotlara** (generic metotlar ve `ref`/`out` parametreler dahil) sahip olmasını sağlar.

---

## Neden var

Runtime'da tip üreten araçların çoğu iki kategoriden birine düşer:

- **Sadece veri üretenler**: property bag'ler, dictionary sarmalayıcılar. Hızlıdır ama üretilen "nesne" hiçbir zaman gerçek bir interface'e cast edilemez — sizi güçlü-tipli API'lerin (bir metodun `IValidatable` beklemesi gibi) dışında bırakır.
- **Tam interceptor/proxy mekanizmaları**: her metot çağrısını tek, merkezi bir "interceptor" fonksiyonuna yönlendirir. Esnektir ama genellikle ağırdır, ve "bu tek metoda özel, şu implementasyonu ata" gibi ince-taneli bir kontrol sunmaz — tüm çağrılar aynı merkezi noktadan geçer.

`DSO.Core.Evoker.Extend` ortada bir yol izliyor: **her metoda kendi delegate'inizi ayrı ayrı atayabiliyorsunuz** (`SetMethod("Validate", () => true)` gibi), ama bu bir "hepsi bir interceptor'dan geçsin" mimarisi değil — her metot kendi küçük, bağımsız forwarder'ına sahip. Property'ler için zaten var olan sıfır-alloc erişim katmanı (`DynamicEntityAccessor`) burada da aynen kullanılıyor; metot çağırma tarafında ise gerçek CLR event'leri, gerçek indexer'lar ve gerçek generic metotlar üretiyoruz — taklit değil, birebir.

---

## Öne çıkan özellikler

### 1. Property'lerin YANINDA gerçek davranış
Çoğu "dinamik tip" aracı sizi salt-veri property'lerle sınırlar. Burada bir interface'in **gerçek bir metodunu** (`bool Validate();` gibi) implement edip, ona istediğiniz an bir C# delegate'i (lambda ya da gerçek bir metot) atayabilirsiniz. Atamazsanız, çağrıldığında sessiz bir `NullReferenceException` değil, **açık ve anlaşılır** bir `InvalidOperationException` alırsınız.

### 2. Generic metotlar — çoğu benzer aracın atladığı bir köşe durumu
`T Get<T>()` gibi bir generic interface metodunu implement etmek, çoğu runtime-tip-üretme yaklaşımının basitçe desteklemediği bir şey (bir generic metodun açık tip parametresi, statik olarak başka bir yere taşınamaz). Biz bunu **type-erasure** deseniyle (`Func<Type[], object?[], object?>`) çözdük — `Echo<int>(42)` ve `Echo<string>("x")` **aynı** delegate üzerinden, doğru tiplerle çalışır.

### 3. `ref`/`out` parametreler — nadiren desteklenen bir başka köşe durumu
`bool TryGet<T>(string key, out T value)` gibi hem generic hem `ref`/`out` olan bir metot bile çalışır — runtime'da özel bir delegate tipi sentezleyip (`Func<>`/`Action<>`'ın ifade edemediği imzalar için), değerleri gerçek anlamda çağırana geri taşıyoruz.

### 4. Gerçek CLR event'leri ve indexer'ları
`event EventHandler Changed;` implement ettiğinizde, `nesne.Changed += handler;` **normal C# syntax'ıyla** çalışır — bizim özel bir API'mizi öğrenmenize gerek yok. Aynı şekilde `this[string key]` gibi indexer'lar, interface üzerinden normal `[]` syntax'ıyla erişilebilir.

### 5. Net hata mesajları, sessiz başarısızlık yok
Bu kütüphaneyi geliştirirken karşılaştığımız her CLR/Reflection.Emit kısıtını (interface metotlarının implicit değil explicit bağlanması gerektiği, metotların virtual olması zorunluluğu, generic metotlarda IL seviyesinde branch+throw'un çökmesi gibi) sizin yerinize zaten yaşadık ve çözdük — siz bunlarla hiç karşılaşmayacaksınız.

---

## Hızlı başlangıç

```csharp
using DSO.Core.Evoker;
using DSO.Core.Evoker.Extend;

public interface IValidatable
{
    int Id { get; set; }
    bool Validate();
}

var dc = DynamicClass.CreateClass("Kayit").Implement<IValidatable>();

dc.SetValue<int>("Id", 42);
dc.SetMethod<Func<bool>>("Validate", () => dc.GetValue<int>("Id") > 0);

IValidatable typed = dc.As<IValidatable>();
Console.WriteLine(typed.Validate()); // true
```

---

## Kapsamlı Özellik Rehberi

### `Implement<TInterface>()` — bir interface'i implement et

```csharp
public interface ICustomer
{
    int Id { get; set; }
    string Name { get; set; }
}

var dc = DynamicClass.CreateClass("Customer").Implement<ICustomer>();
// Id ve Name OTOMATİK olarak şemaya eklendi - elle AddProperty çağırmanıza gerek yok.

dc.SetValue<int>("Id", 1);
ICustomer typed = dc.As<ICustomer>(); // tip-güvenli cast, implement edilmemişse InvalidCastException
```

> **Kısıt:** `TInterface` **public** olmalıdır. Dinamik tip ayrı bir assembly'de üretiliyor; `internal` bir interface'i implement etmeye çalışırsanız `TypeLoadException` alırsınız.

### Property-only olmayan interface'ler — gerçek metotlar

```csharp
public interface IValidatable
{
    bool Validate();                          // gerçek davranış
}

var dc = DynamicClass.CreateClass("X").Implement<IValidatable>();
dc.SetMethod<Func<bool>>("Validate", () => true);   // atamazsanız çağrıldığında net bir hata alırsınız
```

### `Extend<TBase>()` — var olan bir sınıftan türe

```csharp
public abstract class Animal
{
    public abstract string Name { get; set; }
    public string Describe() => $"Ben {Name}";   // SOMUT metot - CLR'ın normal inheritance'ıyla otomatik miras alınır
}

var dc = DynamicClass.CreateClass("Kopek").Extend<Animal>();
dc.SetValue<string>("Name", "Karabaş");

var typed = (Animal)dc.RawInstance;
Console.WriteLine(typed.Describe()); // "Ben Karabaş"
```

**Kısıtlar:** Base class sealed olamaz; erişilebilir (public/protected) **parametresiz** bir constructor'ı olmalı; abstract üyeler sadece property veya metot olabilir (event/indexer'lı abstract üyeler henüz desteklenmiyor).

### Aynı anda hem `Extend` hem `Implement`

```csharp
var dc = DynamicClass.CreateClass("X")
    .Extend<Animal>()
    .Implement<IAuditable>();   // ikisi de AYNI instance üzerinde çalışır
```

### Generic metotlar

```csharp
public interface IContainer
{
    T Echo<T>(T value);
    bool TryGet<T>(string key, out T value);              // generic + out
    void Increment<T>(ref T value, T amount) where T : struct; // generic + ref
}

var dc = DynamicClass.CreateClass("X").Implement<IContainer>();

dc.SetGenericMethod("Echo", (typeArgs, args) => args[0]);

dc.SetGenericMethod("TryGet", (typeArgs, args) =>
{
    string key = (string)args[0]!;
    var holder = (object?[])args[1]!;   // 'out T value' -> args[1] TEK ELEMANLI bir holder
    if (Sozluk.TryGetValue(key, out var v)) { holder[0] = v; return true; }
    // ÖNEMLİ: T value type olabilir - holder[0]'a ÇIPLAK null KOYMAYIN (NullReferenceException alırsınız).
    holder[0] = typeArgs[0].IsValueType ? Activator.CreateInstance(typeArgs[0]) : null;
    return false;
});

var typed = dc.As<IContainer>();
bool bulundu = typed.TryGet<int>("yas", out int yas);   // normal C# out syntax'ı!
```

### Event'ler

```csharp
public interface IObservable
{
    event EventHandler Changed;
}

var dc = DynamicClass.CreateClass("X").Implement<IObservable>();
var typed = dc.As<IObservable>();

typed.Changed += (s, e) => Console.WriteLine("değişti!");
dc.RaiseEvent("Changed", dc.RawInstance, EventArgs.Empty); // "değişti!"
```

### Indexer'lar

```csharp
public interface IBag
{
    int this[string key] { get; set; }
}

var dc = DynamicClass.CreateClass("X").Implement<IBag>();
var store = new Dictionary<string, int>();
dc.SetMethod<Func<string, int>>("get_Item", k => store[k]);
dc.SetMethod<Action<string, int>>("set_Item", (k, v) => store[k] = v);

var typed = dc.As<IBag>();
typed["a"] = 10;                 // normal indexer syntax'ı!
```

### Standalone kullanım (interface/base class olmadan)

`DSO.Core.Evoker`'ın kendisi zaten `AddMethod`/`AddGenericMethod`/`AddEvent` gibi ilkel API'leri sunuyor — `Implement<T>`/`Extend<T>` bunların üzerine sadece "interface'i tara, otomatik ekle, `DefineMethodOverride` ile bağla" katmanını koyuyor. Bir interface'e ihtiyacınız yoksa doğrudan core'un API'sini kullanabilirsiniz (bkz. [`DSO.Core.Evoker` README'si](../DSO.Core.Evoker/README.md)).

---

## Bilinen Sınırlar (dürüstçe)

| Durum | Destekleniyor mu |
|---|---|
| Property-only interface (get/set) | ✅ |
| Gerçek davranışlı metotlar | ✅ |
| Generic metotlar (`T Get<T>()`) | ✅ |
| `ref`/`out` parametreler | ✅ |
| Generic + `ref`/`out` birlikte | ✅ |
| Event'ler | ✅ |
| Indexer'lar (`this[index]`) | ✅ |
| Aşırı yüklenmiş (overloaded) indexer'lar (`this[int]` + `this[string]`) | ❌ |
| `static abstract` interface üyeleri (C# 11/.NET 7+) | ❌ (.NET 6'da runtime desteği yok) |
| Base class'ta abstract event/indexer | ❌ |
| Parametreli (parametresiz olmayan) base class constructor'ı | ❌ |
| `internal` interface implementasyonu | ❌ |

## Gereksinimler

.NET 6.0+ · [`DSO.Core.Evoker`](../DSO.Core.Evoker/README.md) (proje referansı)
