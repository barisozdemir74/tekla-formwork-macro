# Tekla Formwork Macro

Tekla Structures için kalıp alanı hesaplama ve CSV çıktısı üretme çalışması.

Bu depo, betonarme elemanların **kalıp gören yüzey alanlarını** hesaplamak, sonuçları CSV dosyasına yazmak ve mümkünse Tekla Organizer / User Defined Attributes alanlarıyla ilişkilendirmek için hazırlanmıştır.

---

## Amaç

Bu makronun amacı:

- Seçilen veya modeldeki uygun betonarme elemanları okumak
- Eleman tipine göre kalıp alanı hesaplamak
- Tekla'nın kendi `AREA` değeriyle karıştırmadan ayrı bir **kalıp alanı** değeri üretmek
- Sonuçları CSV dosyasına aktarmak
- Hesaplanan değerleri kontrol edilebilir şekilde raporlamak

---

## Ana Dosyalar

| Dosya | Açıklama |
|---|---|
| `FormworkHesap.cs` | Tekla makro kaynak kodu |
| `objects.inp` | Tekla UDA alan tanımları |
| `formwork_output_template.csv` | CSV çıktı şablonu |
| `README.md` | Proje açıklaması |

---

## Beklenen CSV Kolonları

CSV dosyasında aşağıdaki kolonların bulunması hedeflenir:

| Kolon | Açıklama |
|---|---|
| `Tekla_GUID` | Elemanın Tekla GUID değeri |
| `Tekla_ID` | Elemanın Tekla iç ID değeri |
| `Name` | Eleman adı |
| `Class` | Tekla class değeri |
| `Type` | Eleman tipi |
| `Profile` | Profil bilgisi |
| `Material` | Malzeme bilgisi |
| `Level` | Kot / seviye bilgisi |
| `Phase` | Faz bilgisi |
| `FW_CAT` | Kalıp kategorisi |
| `FW_AREA_M2` | Hesaplanan kalıp alanı, m² |
| `FW_RULE` | Kullanılan hesap kuralı |
| `FW_CONF` | Hesap güven seviyesi |
| `FW_REVIEW_TXT` | Kontrol açıklaması |
| `FW_UPDATED` | Güncelleme tarihi |

---

## Önemli Not

Tekla'da görünen standart `AREA` değeri ile bu makronun ürettiği `FW_AREA_M2` değeri aynı şey değildir.

- `AREA`: Tekla'nın kendi geometri / yüzey alanı değeri olabilir.
- `FW_AREA_M2`: Kalıp hesabı için özel üretilen değerdir.

Bu nedenle raporlamada esas alınacak değer:

```text
FW_AREA_M2
```

olmalıdır.

---

## Kullanım Mantığı

1. Tekla modelini aç.
2. Hesaplanacak betonarme elemanları seç.
3. Makroyu çalıştır.
4. CSV çıktısının oluştuğunu kontrol et.
5. CSV dosyasındaki `FW_AREA_M2` değerlerini incele.
6. Gerekirse `FW_REVIEW_TXT` kolonundaki uyarıları kontrol et.

---

## Geliştirme Hedefleri

- [ ] Makro çalışmaya başladığında kullanıcıya bilgi mesajı göster
- [ ] Yalnızca seçili elemanlar için çalışma seçeneği ekle
- [ ] CSV dosyasının oluşmama hatasını düzelt
- [ ] Organizer ile uyumlu UDA alanlarını kontrol et
- [ ] `FW_AREA_M2` değerini Tekla standart `AREA` değerinden tamamen ayır
- [ ] Kolon adlarını sabitle
- [ ] Hatalı / eksik geometri için kontrol mesajı üret
- [ ] Örnek çıktı dosyası ekle

---

## Önerilen Repository Yapısı

```text
tekla-formwork-macro/
│
├── README.md
├── FormworkHesap.cs
├── objects.inp
├── formwork_output_template.csv
│
├── docs/
│   └── notes.md
│
└── samples/
    └── sample-output.csv
```

---

## Durum

Bu proje geliştirme aşamasındadır.

Öncelikli hedef:

```text
CSV dosyasının güvenilir şekilde oluşması ve FW_AREA_M2 değerinin doğru yazılması.
```
