# Deploy στο SmarterASP.NET

Οδηγός για ανέβασμα του MoneyTracker σε **SmarterASP.NET** (Windows / IIS shared hosting με SQL Server).

Η εφαρμογή είναι ASP.NET Core 8. Το SmarterASP τρέχει ASP.NET Core apps μέσω IIS
(ASP.NET Core Module) και δίνει SQL Server database από το control panel.

> Στο πρώτο τρέξιμο η εφαρμογή **δημιουργεί μόνη της τους πίνακες** και το import profile.
> Δεν χρειάζεται να τρέξεις SQL script με το χέρι.

---

## 0. Τι θα χρειαστείς

- Λογαριασμό SmarterASP.NET (υπάρχει και free trial).
- Τοπικά **.NET SDK 8.0** για να κάνεις publish (`dotnet --version` → 8.x).
- Τα στοιχεία της SQL βάσης (τα φτιάχνεις στο βήμα 1).

---

## 1. Δημιούργησε τη SQL Server βάση (control panel)

1. Μπες στο SmarterASP Control Panel → **Database Manager → MSSQL** (v10 control panel:
   <https://www.smarterasp.net/support/kb/c165/control-panel-v10.aspx>).
2. **Create Database** → κράτησε:
   - Server (π.χ. `SQL5xxx.site4now.net`)
   - Database name (π.χ. `db_xxxxx_moneytracker`)
   - User / Password
3. Το connection string θα έχει τη μορφή:

   ```
   Data Source=SQL5xxx.site4now.net;Initial Catalog=db_xxxxx_moneytracker;User Id=db_xxxxx_user;Password=********;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=True
   ```

   > Στο shared hosting βάλε **`Encrypt=False`** (ή `TrustServerCertificate=True`) — αλλιώς το
   > Microsoft.Data.SqlClient μπορεί να αποτύχει στην επαλήθευση πιστοποιητικού.

---

## 2. Ρύθμισε τα secrets πριν το publish

Στο `src/MoneyTracker/appsettings.json` βάλε το connection string και **άλλαξε τον κωδικό**:

```json
"ConnectionStrings": {
  "Default": "Data Source=SQL5xxx.site4now.net;Initial Catalog=db_xxxxx_moneytracker;User Id=db_xxxxx_user;Password=********;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=True"
},
"Auth": {
  "Username": "admin",
  "Password": "ΒΑΛΕ-ΕΝΑΝ-ΔΙΚΟ-ΣΟΥ-ΙΣΧΥΡΟ-ΚΩΔΙΚΟ"
}
```

> Εναλλακτικά (πιο ασφαλές) μπορείς να τα βάλεις ως **environment variables** στο `web.config`
> στον server μετά το ανέβασμα — δες το βήμα 6.

---

## 3. Publish τοπικά

Από τον φάκελο του project:

```bash
dotnet publish src/MoneyTracker/MoneyTracker.csproj -c Release -o publish
```

Αυτό παράγει στον φάκελο `publish/` όλα τα αρχεία μαζί με το **`web.config`** (δημιουργείται
αυτόματα για το IIS/ASP.NET Core Module) και το `MoneyTracker.exe`.

> **Αν το SmarterASP δεν έχει το .NET 8 runtime** (θα δεις σφάλμα 500.30/500.31 στο βήμα 7),
> κάνε self-contained publish αντ' αυτού:
> ```bash
> dotnet publish src/MoneyTracker/MoneyTracker.csproj -c Release -r win-x64 --self-contained true -o publish
> ```

---

## 4. Ανέβασε τα αρχεία

Στο Control Panel → **File Manager** (ή με FTP):

1. Πήγαινε στον **root φάκελο του site** (εκεί που πρέπει να κάθεται το `web.config`,
   συνήθως `\` ή `wwwroot` του domain — στο SmarterASP είναι ο root του site, ΟΧΙ υποφάκελος).
2. Ανέβασε **όλο το περιεχόμενο** του τοπικού `publish/` (όχι τον ίδιο τον φάκελο — τα αρχεία μέσα του).
   - Εύκολος τρόπος: κάνε zip το περιεχόμενο του `publish/`, ανέβασέ το με το File Manager και **Extract** στον root.
3. Βεβαιώσου ότι στον root υπάρχουν `web.config`, `MoneyTracker.dll`, `appsettings.json`, φάκελος `wwwroot/`.

---

## 5. App pool = ASP.NET Core

Το SmarterASP συνήθως αναγνωρίζει αυτόματα ASP.NET Core apps. Αν υπάρχει επιλογή για
.NET version στο control panel, όρισε την σε **"No Managed Code" / .NET Core**
(το ANCM του `web.config` αναλαμβάνει το υπόλοιπο).

---

## 6. (Προαιρετικό) Secrets μέσω web.config

Αντί να αφήσεις κωδικούς στο `appsettings.json`, μπορείς να τα βάλεις ως env vars.
Στο `web.config` μέσα στο `<aspNetCore ...>`:

```xml
<aspNetCore processPath=".\MoneyTracker.exe" stdoutLogEnabled="false" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ConnectionStrings__Default" value="Data Source=...;Encrypt=False;TrustServerCertificate=True" />
    <environmentVariable name="Auth__Password" value="ο-κωδικος-σου" />
  </environmentVariables>
</aspNetCore>
```

(Το `__` αντιστοιχεί στο `:` της ιεραρχίας του configuration.)

---

## 7. Άνοιξε το site

1. Μπες στο temporary URL του SmarterASP (ή στο domain σου).
2. Στο πρώτο άνοιγμα η εφαρμογή **δημιουργεί τους πίνακες** στη βάση και το Piraeus import profile.
3. Σύνδεση με το `Auth:Username` / `Auth:Password` που όρισες.
4. Ανέβασε ένα Excel και δοκίμασε το import.

---

## Troubleshooting

| Σύμπτωμα | Αιτία / Λύση |
| --- | --- |
| **HTTP 500.30 / 500.31** | Λείπει το .NET 8 runtime → κάνε **self-contained** publish (βήμα 3). |
| **HTTP 500.19** | Πρόβλημα στο `web.config` / δεν είναι εγκατεστημένο το ASP.NET Core Module → επιβεβαίωσε ότι ανέβηκε το `web.config` και ότι το app είναι ASP.NET Core. |
| **Σφάλμα σύνδεσης SQL** | Λάθος connection string ή SSL → βάλε `Encrypt=False;TrustServerCertificate=True`, τσέκαρε server/user/password. |
| **Cannot open database / login failed** | Ο χρήστης δεν έχει δικαιώματα στη βάση, ή λάθος όνομα βάσης. |
| **Δεν κρατάει το login / γυρνάει στη σελίδα σύνδεσης** | Τα Data Protection keys πρέπει να αποθηκεύονται σε φάκελο εντός της εφαρμογής (η εφαρμογή φτιάχνει τον φάκελο `keys/` στον root). Βεβαιώσου ότι ο root είναι εγγράψιμος (είναι, μέσω File Manager). |
| **Θέλω να δω σφάλματα** | Στο `web.config` βάλε `stdoutLogEnabled="true"` προσωρινά και δες το `logs/` στον root. |

---

## Updates αργότερα

Για νέα έκδοση: `dotnet publish` ξανά και ανέβασε τα αρχεία (overwrite). Το schema
ενημερώνεται αυτόματα για νέες στήλες (idempotent patch στο startup). Για μεγάλες αλλαγές
schema, δες `docs/DESIGN.md §4` (migrations).
