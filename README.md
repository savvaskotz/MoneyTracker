# MoneyTracker

Προσωπική web εφαρμογή παρακολούθησης εξόδων. Ενιαία εφαρμογή **ASP.NET Core 8 (Razor Pages)**
με **Entity Framework Core** και **SQL Server**, server-side rendering, χωρίς ξεχωριστό frontend/backend.

Ο σχεδιασμός (schema, αποφάσεις, αλγόριθμοι) περιγράφεται στο [`docs/DESIGN.md`](docs/DESIGN.md).

## Δυνατότητες (v1)

- Εισαγωγή κινήσεων από Excel (ClosedXML) με **configurable format** ανά τράπεζα.
- Προεπιλεγμένο profile για **Τράπεζα Πειραιώς – Πιστωτική κάρτα**.
- **Preview** πριν την αποθήκευση· αλλαγή κατηγορίας ανά κίνηση· επιβεβαίωση.
- **Duplicate detection** με deterministic SHA-256 fingerprint + occurrence ordinal + unique index.
- **Ιεραρχικές κατηγορίες** (έως 3 επίπεδα) με έλεγχο βάθους & αποφυγή κύκλων.
- **Πρόταση κατηγορίας**: κανόνες χρήστη → ιστορικό → εκμάθηση → κατηγορία τράπεζας → «Δεν βρέθηκε».
- **Εκμάθηση** από τις διορθώσεις του χρήστη.
- **Dashboard** με έσοδα/έξοδα/υπόλοιπο, έξοδα ανά κατηγορία (hierarchical roll-up), ανά μήνα,
  ανά έμπορο, μεγαλύτερα έξοδα, και φίλτρα ημερομηνίας.
- **Authentication** (single hard-coded user), HTTPS, antiforgery, έλεγχος uploads.

## Προαπαιτούμενα

- .NET SDK 8.0
- SQL Server (LocalDB, Express, Developer ή κανονικό instance)

## Ρύθμιση

Επεξεργάσου το `src/MoneyTracker/appsettings.json`:

```json
"ConnectionStrings": {
  "Default": "Server=localhost;Database=MoneyTracker;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
},
"Auth": {
  "Username": "admin",
  "Password": "MoneyTracker#2026"
}
```

> **Ασφάλεια:** για production μην αφήνεις credentials/connection string μέσα στο `appsettings.json`.
> Χρησιμοποίησε user-secrets (dev) ή environment variables (prod), π.χ.
> `dotnet user-secrets set "Auth:Password" "..."` και
> `dotnet user-secrets set "ConnectionStrings:Default" "..."`.

### Στοιχεία σύνδεσης (προεπιλογή)

| | |
| --- | --- |
| **Username** | `admin` |
| **Password** | `MoneyTracker#2026` |

Άλλαξέ τα από το `Auth` section πριν το deployment.

## Εκτέλεση

```bash
cd src/MoneyTracker
dotnet run
```

Άνοιξε το `https://localhost:7180`. Η βάση και τα δεδομένα αναφοράς (import profile)
δημιουργούνται αυτόματα στο πρώτο τρέξιμο.

## Ροή χρήσης

1. Σύνδεση με τα παραπάνω credentials.
2. **Εισαγωγή Excel** → ανέβασε το `.xlsx` της τράπεζας.
3. Έλεγξε το **Preview**, διόρθωσε κατηγορίες, πάτησε **Επιβεβαίωση**.
4. Δες αναλύσεις στο **Dashboard**· διαχειρίσου δέντρο κατηγοριών στις **Κατηγορίες**.

## Δομή

```
src/MoneyTracker/
├── Domain/         # Entities + enums
├── Data/           # DbContext + seeder
├── Services/       # Import, Categorization, Duplicates, Dashboard
├── Pages/          # Razor Pages (Dashboard, Import, Transactions, Categories, Account)
└── wwwroot/        # CSS
```

## Βάση δεδομένων / migrations

Η v1 δημιουργεί το schema με `EnsureCreated()` στο startup (τρέχει χωρίς EF tooling).
Για versioned migrations:

```bash
dotnet tool install --global dotnet-ef
# αφαίρεσε το EnsureCreated() από το Program.cs και βάλε db.Database.Migrate()
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Δες [`docs/DESIGN.md`](docs/DESIGN.md) §4.
