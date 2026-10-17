# SQL Yedekleme Aracı

SQL Server (Express ve üzeri) kullanıcı veritabanlarını otomatik yedekler, sağlamlık kontrolü yapar, ZIP’ler ve saklama kuralına göre eskileri siler. Türkçe arayüz; Windows 10/11 ve **Windows Server** için tak-çalıştır kurulum.

## Windows Server — birkaç ayar, tak çalıştır

1. Bu klasörü sunucuya kopyalayın (`dist\SQLYedeklemeAraci.exe` dahil).
2. İsterseniz `basit-ayarlar.json` dosyasını Not Defteri ile düzenleyin:

```json
{
  "Sunucu": ".\\SQLEXPRESS",
  "YedekKlasoru": "C:\\SQLBackup",
  "SaklamaAdedi": 7,
  "Saat": "02:00",
  "Eposta": {
    "Acik": false,
    "SmtpSunucu": "smtp.ornek.com",
    "SmtpPort": 587,
    "Ssl": true,
    "Kimden": "yedek@ornek.com",
    "Kime": "yonetici@ornek.com",
    "SmtpKullanici": "",
    "SmtpParola": "",
    "BasaridaGonder": true,
    "HatadaGonder": true
  }
}
```

| Alan | Anlamı | Varsayılan |
|------|--------|------------|
| `Sunucu` | SQL örneği (`.\SQLEXPRESS` veya `(local)`) | `.\SQLEXPRESS` |
| `YedekKlasoru` | Yerel veya UNC kök — yedekler `...\SQLBackup\` altına gider | `C:\SQLBackup` |
| `SaklamaAdedi` | Veritabanı başına tutulacak yedek sayısı | `7` |
| `Saat` | Günlük görev saati | `02:00` |
| `Eposta.Acik` | Başarı/hata maili gönder | `false` (kapalı) |
| `Eposta.SmtpSunucu` / `SmtpPort` / `Ssl` | SMTP sunucusu | `587` + SSL |
| `Eposta.Kimden` / `Kime` | Gönderen / alıcı (birden fazla: `;` veya `,`) | — |
| `Eposta.SmtpKullanici` / `SmtpParola` | İsteğe bağlı SMTP kimlik | boş = kimliksiz |
| `Eposta.BasaridaGonder` | “yedekleme başarılı” maili | `true` |
| `Eposta.HatadaGonder` | Hata maili | `true` |

`SmtpParola` yalnızca `--apply-basit` sırasında bir kez düz metin okunur; `config.json` içinde **DPAPI** ile saklanır. Sonraki uygulamalarda boş bırakırsanız eski parola korunur. Mail gönderilemezse yedekleme sonucu **değişmez** (best-effort).

3. **`Kurulum-Sunucu.bat`** dosyasına çift tıklayın → UAC’de **Evet**.
4. Açılan uygulamada **Test Yedeği Al** → kutular yeşilse bitti.

Google Drive **zorunlu değil**. Sunucuda varsayılan hedef yerel klasördür. Oturum kapalı sunucuda: Ayarlar → **Sunucu Modu** → “Oturum açılmadan” veya “SYSTEM” (SYSTEM için SQL oturum bilgisi gerekir).

Elle ayar: `Uygula-Ayarlar.ps1` veya  
`SQLYedeklemeAraci.exe --apply-basit basit-ayarlar.json`  
Elle yedek: `SQLYedeklemeAraci.exe --backup` (çıkış 0 = başarılı).

## Masaüstü (Google Drive) kullanıcıları

1. [Google Drive for Desktop](https://www.google.com/drive/download/) kurup kişisel hesapla giriş yapın.
2. **`setup.bat`** → sihirbazı izleyin.

Ayrıntılar: **HIZLI_BASLANGIC.md**, **KURULUM_AYARLAR.md**, güvenlik: **GUVENLIK.md**.

## Bu pakette ne var?

| Dosya | Ne işe yarar |
|-------|----------------|
| `Kurulum-Sunucu.bat` | Server tak-çalıştır |
| `setup.bat` / `setup.ps1` | Genel kurulum |
| `basit-ayarlar.json` | Basit ayarlar (+ isteğe bağlı `Eposta`) |
| `src/MailNotifier.cs` | SMTP başarı/hata bildirimi |
| `dist\SQLYedeklemeAraci.exe` | Hazır uygulama (self-contained) |
| `src\` | .NET 8 WinForms kaynak |
| `backup.master.sql` | Gömülü yedek betiği |
| `tests\` | Mac/Linux’ta da çalışan paket doğrulama |
| `.github/workflows\` | `windows-latest` + SQL smoke (GitHub’a push sonrası) |
