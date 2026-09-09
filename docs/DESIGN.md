# MoneyTracker — Προτεινόμενο Design & Architecture

> Έγγραφο σχεδίασης **πριν** την υλοποίηση, όπως ζητήθηκε στην §20 του spec.
> Στόχος: να συμφωνήσουμε σε schema, entities, migration strategy, import architecture,
> duplicate detection, category suggestion/learning και import preview **πριν γραφτεί σημαντικός κώδικας**.
>
> **Αρχή:** απλό & maintainable. Χωρίς over-engineering, χωρίς microservices, χωρίς REST API.

---

## 0. Συνοπτικές αποφάσεις (TL;DR)

| Θέμα | Απόφαση | Γιατί |
| --- | --- | --- |
| Πλατφόρμα | Ενιαία εφαρμογή **ASP.NET Core (.NET 8 LTS)** | Ένα project, server-side rendering (§1) |
| UI | **Razor Pages** | Form-heavy CRUD app· λιγότερο boilerplate από MVC για αυτό το use case |
| ORM | **Entity Framework Core** (Code-First) | Migrations, καθαρό mapping (§19) |
| DB | **Microsoft SQL Server** | Απαίτηση (§1, §19) |
| Excel | **ClosedXML** | Χωρίς εξάρτηση από Excel/Interop, ασφαλές parsing (§19) |
| Auth | **ASP.NET Core Identity** | Απαίτηση authentication/authorization (§18) |
| Amount model | `Amount` θετικό magnitude + `Type` (Expense/Income) | Ξεκάθαρα aggregations, χωρίς σύγχυση προσήμου |
| Duplicate | Deterministic **Fingerprint (SHA-256)** + occurrence ordinal + **unique index** | Δύο επίπεδα προστασίας (§7) |
| Format Excel | **Configurable ImportProfile** (column mapping) | Διαφορετικές τράπεζες (§6) |
| Preview | **Staging table** ανά Import (όχι session) | Ανθεκτικό σε reload, χωρίς re-parse (§8, §13) |
| Suggestion | Rules → History → Normalized merchant → Uncategorized | Σειρά προτεραιότητας (§12) |

---

## 1. Αρχιτεκτονική (high level)

```
Browser
  → ASP.NET Core (Razor Pages)
      → Services (Import / Normalization / DuplicateDetection / CategorySuggestion / Dashboard)
      → EF Core DbContext
  → SQL Server
```

Καμία εξωτερική κλήση API. Όλα τα δεδομένα μένουν στο hosting του χρήστη (§18).

### Προτεινόμενη δομή project (ένα project)

```
MoneyTracker/
├── src/MoneyTracker/
│   ├── Program.cs
│   ├── appsettings.json                # ΧΩΡΙΣ secrets/connection string με credentials
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   └── Migrations/
│   ├── Domain/                         # Entities + enums
│   │   ├── Category.cs
│   │   ├── Transaction.cs
│   │   ├── Account.cs
│   │   ├── ImportBatch.cs
│   │   ├── ImportStagingRow.cs
│   │   ├── CategoryRule.cs
│   │   └── ImportProfile.cs
│   ├── Services/
│   │   ├── Import/ImportService.cs
│   │   ├── Import/ExcelReader.cs
│   │   ├── Categorization/NormalizationService.cs
│   │   ├── Categorization/CategorySuggestionService.cs
│   │   ├── Categorization/CategoryLearningService.cs
│   │   ├── Duplicates/FingerprintService.cs
│   │   └── Dashboard/DashboardService.cs
│   └── Pages/
│       ├── Import/ (Upload, Preview, Result)
│       ├── Transactions/
│       ├── Categories/
│       └── Dashboard/
└── docs/DESIGN.md
```

Ελάχιστα services, καθαρές ευθύνες — χωρίς layers που δεν χρειάζονται.

---

## 2. Database Schema

### 2.1 `Categories` — hierarchical, max depth 3

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `int` IDENTITY PK | |
| `Name` | `nvarchar(100)` NOT NULL | |
| `ParentId` | `int` NULL → `Categories.Id` | root όταν NULL |
| `Depth` | `tinyint` NOT NULL | 1..3, συντηρείται από την εφαρμογή· επιταχύνει checks & queries |
| `NormalizedName` | `nvarchar(100)` NOT NULL | για uniqueness/lookup |
| `CreatedAt` | `datetime2` NOT NULL | |

- FK `ParentId` με `ON DELETE NO ACTION` (αποφυγή cascade σε δέντρο).
- Unique index: `(ParentId, NormalizedName)` → όχι δύο ίδια αδέλφια.
- **Depth ≤ 3** και **no circular hierarchy**: enforced στην εφαρμογή (βλ. §5).

> Επιλογή: κρατάμε **adjacency list** (`ParentId`). Είναι το πιο απλό & maintainable. Επειδή
> το βάθος είναι το πολύ 3, τα descendant queries είναι φθηνά (max 2 joins) και δεν χρειάζεται
> materialized path ή recursive CTE-heavy σχεδίαση. (Αν χρειαστεί αργότερα, το προσθέτουμε.)

### 2.2 `Accounts` — Account/Card (extensible)

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `int` IDENTITY PK | |
| `Name` | `nvarchar(100)` | π.χ. "Alpha Bank – Card 1234" |
| `Identifier` | `nvarchar(100)` | σταθερό αναγνωριστικό (IBAN/κάρτα masked) |
| `CreatedAt` | `datetime2` | |

Ξεκινάμε με 1 account, αλλά ο πίνακας υπάρχει από τώρα ώστε το fingerprint & το dashboard να είναι
έτοιμα για πολλαπλούς λογαριασμούς/κάρτες (§17) χωρίς μελλοντικό breaking change.

### 2.3 `Transactions` — βασικός πίνακας

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `bigint` IDENTITY PK | |
| `AccountId` | `int` NOT NULL → `Accounts` | |
| `TransactionDate` | `date` NOT NULL | ημερομηνία κίνησης |
| `Amount` | `decimal(18,2)` NOT NULL | **θετικό magnitude** (≥ 0) |
| `Type` | `tinyint` NOT NULL | `Expense=1`, `Income=2` |
| `CategoryId` | `int` NULL → `Categories` | NULL = Uncategorized |
| `Description` | `nvarchar(400)` NOT NULL | καθαρή περιγραφή που δείχνουμε |
| `OriginalDescription` | `nvarchar(1000)` NOT NULL | raw από Excel — δεν αλλοιώνεται |
| `NormalizedDescription` | `nvarchar(400)` NOT NULL | για matching/suggestion |
| `Fingerprint` | `char(64)` NOT NULL | SHA-256 hex (§7) |
| `ImportBatchId` | `int` NOT NULL → `ImportBatch` | προέλευση |
| `CurrencyCode` | `char(3)` NOT NULL DEFAULT 'EUR' | future-proof |
| `CreatedAt` | `datetime2` NOT NULL | |

Indexes:
- **Unique** `(AccountId, Fingerprint)` → δεύτερο επίπεδο προστασίας duplicate (§7).
- `(TransactionDate)` → date filtering dashboard (§16).
- `(CategoryId)` → aggregations ανά κατηγορία.
- `(NormalizedDescription)` → history-based suggestion (§9).

> **Απόφαση Amount + Type αντί για signed amount:** αποθηκεύουμε πάντα θετικό ποσό και ξεχωριστό
> `Type`. Τα aggregations γίνονται καθαρά (`SUM(Amount) WHERE Type=Expense`) χωρίς κίνδυνο λάθους
> προσήμου από διαφορετικά bank formats. Το πρόσημο/στήλες debit-credit του Excel το ερμηνεύει
> το `ImportProfile` (§6) και το μετατρέπει σε `Amount`+`Type`.

### 2.4 `ImportBatch` — παρακολούθηση import

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `int` IDENTITY PK | |
| `AccountId` | `int` NOT NULL → `Accounts` | |
| `ImportProfileId` | `int` NULL → `ImportProfile` | ποιο format χρησιμοποιήθηκε |
| `FileName` | `nvarchar(260)` | |
| `Status` | `tinyint` | `Pending=1`, `Completed=2`, `Cancelled=3`, `Failed=4` |
| `TotalRows` | `int` | |
| `NewCount` | `int` | |
| `DuplicateCount` | `int` | |
| `CreatedAt` | `datetime2` | |
| `CompletedAt` | `datetime2` NULL | |

### 2.5 `ImportStagingRow` — preview candidates (πριν το confirm)

Κρατάει τα parsed candidates ανά import **πριν** μπουν στα `Transactions`. Το preview & το confirm
διαβάζουν από εδώ (χωρίς re-parse του αρχείου, ανθεκτικό σε reload).

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `bigint` IDENTITY PK | |
| `ImportBatchId` | `int` NOT NULL → `ImportBatch` | |
| `RowIndex` | `int` | σειρά στο αρχείο |
| `TransactionDate` | `date` | |
| `Amount` | `decimal(18,2)` | |
| `Type` | `tinyint` | |
| `OriginalDescription` | `nvarchar(1000)` | |
| `NormalizedDescription` | `nvarchar(400)` | |
| `Fingerprint` | `char(64)` | |
| `SuggestedCategoryId` | `int` NULL | πρόταση συστήματος |
| `SelectedCategoryId` | `int` NULL | επιλογή χρήστη (default = suggested) |
| `IsDuplicate` | `bit` | αποτέλεσμα ελέγχου |
| `DuplicateReason` | `nvarchar(50)` NULL | `ExistsInDb` / `DuplicateInFile` |
| `SuggestionSource` | `nvarchar(30)` NULL | Rule/History/Merchant/None (§12) |

Καθαρίζεται μετά το Completed/Cancelled (ή με scheduled cleanup των Pending).

### 2.6 `CategoryRule` — learning / user-defined rules

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `int` IDENTITY PK | |
| `Pattern` | `nvarchar(200)` NOT NULL | κανονικοποιημένο pattern (π.χ. "SKLAVENITIS") |
| `MatchType` | `tinyint` NOT NULL | `Exact=1`, `Contains=2`, `StartsWith=3` (configurable, βλ. §10) |
| `CategoryId` | `int` NOT NULL → `Categories` | |
| `Priority` | `int` NOT NULL | μεγαλύτερο = ισχυρότερο |
| `Source` | `tinyint` NOT NULL | `UserDefined=1`, `Learned=2` |
| `HitCount` | `int` NOT NULL DEFAULT 0 | για tie-breaking & confidence |
| `CreatedAt` / `UpdatedAt` | `datetime2` | |

Unique index: `(Pattern, MatchType)` → upsert κατά το learning.

### 2.7 `ImportProfile` — configurable Excel format (§6)

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `int` IDENTITY PK | |
| `Name` | `nvarchar(100)` | π.χ. "Alpha Bank XLSX" |
| `SheetName` | `nvarchar(100)` NULL | NULL = πρώτο sheet |
| `HeaderRowIndex` | `int` | 0-based/1-based (θα οριστεί) |
| `DateColumn` | `nvarchar(50)` | header name ή column letter |
| `DescriptionColumn` | `nvarchar(50)` | |
| `AmountColumn` | `nvarchar(50)` NULL | single signed column |
| `DebitColumn` / `CreditColumn` | `nvarchar(50)` NULL | εναλλακτικό σχήμα δύο στηλών |
| `DateFormat` | `nvarchar(30)` | π.χ. `dd/MM/yyyy` |
| `Culture` | `nvarchar(10)` | decimal/thousand separators (π.χ. `el-GR`) |
| `AmountSignConvention` | `tinyint` | π.χ. negative=expense |
| `CreatedAt` | `datetime2` | |

Έτσι **δεν** υποθέτουμε κοινό format για όλες τις τράπεζες.

### 2.8 Identity tables

Standard **ASP.NET Core Identity** (`AspNetUsers`, `AspNetRoles`, ...). Single-user αρχικά,
αλλά μας δίνει έτοιμο authentication/authorization/hashing (§18).

### 2.9 ER διάγραμμα (λογικό)

```
AspNetUsers

Accounts 1───* Transactions *───0..1 Categories ──┐ (ParentId self-ref, depth ≤ 3)
   │                  │                            └──*
   │                  *
   │             ImportBatch 1───* ImportStagingRow
   └──────────────────┘
Categories 1───* CategoryRule
ImportProfile 1───* ImportBatch
```

---

## 3. Entities & relationships (EF Core)

- `Category` self-reference: `Parent` / `Children` (`ParentId`).
- `Transaction` → `Account` (required), `Category` (optional), `ImportBatch` (required).
- `ImportBatch` → `Account`, `ImportProfile?`, `Children: ImportStagingRow`.
- `CategoryRule` → `Category`.
- Enums (`TransactionType`, `MatchType`, `RuleSource`, `ImportStatus`) mapped ως `tinyint`.
- `decimal(18,2)` ρητά μέσω Fluent API. `Fingerprint` fixed `char(64)`.
- Unique indexes όπως παραπάνω μέσω Fluent API (`HasIndex(...).IsUnique()`).

---

## 4. Migration strategy

1. **EF Core Code-First migrations** (`dotnet ef migrations add InitialCreate`).
2. Initial migration: όλοι οι πίνακες + indexes + FKs.
3. **Seed** (idempotent, μέσα σε migration ή στο startup):
   - default `Account` ("Default").
   - default `ImportProfile` για το πραγματικό Excel format (μόλις το δούμε).
   - προαιρετικά μερικές root categories.
4. **Prod apply:** παράγουμε idempotent SQL script (`dotnet ef migrations script --idempotent`)
   και τρέχει ελεγχόμενα στο hosting. Στο dev επιτρέπεται `Database.Migrate()` στο startup.
   > Δεν κάνουμε auto-migrate σιωπηλά σε production — προτιμούμε ελεγχόμενο script.
5. Connection string: **user-secrets** σε dev, **environment variable** σε prod. Ποτέ committed.

---

## 5. Category hierarchy: depth & circular checks

Κατά τη **δημιουργία/μετακίνηση** κατηγορίας:

- **Max depth 3:** `Depth(parent) + subtreeHeight(node) ≤ 3`. Δηλαδή δεν αρκεί ο νέος κόμβος
  να είναι ≤ level 3· και ολόκληρο το υποδέντρο του δεν πρέπει να ξεπερνά το level 3 μετά τη μετακίνηση.
- **No circular hierarchy:** ο νέος `Parent` δεν επιτρέπεται να είναι ο ίδιος ο κόμβος ή απόγονός του
  (ανέβασμα του αλυσιδωτού `ParentId` από τον προτεινόμενο parent προς τα πάνω· αν συναντήσουμε τον κόμβο → reject).
- Το `Depth` του κόμβου (και των descendants σε move) ενημερώνεται στην ίδια transaction.

Και τα δύο enforced στο service layer με ξεκάθαρα validation errors (§13).

---

## 6. Excel Import architecture (§6, §13)

Ροή:

```
Upload xlsx
  → ExcelReader (ClosedXML) + ImportProfile (column mapping)
  → List<RawRow>
  → Validate (ημ/νία, ποσό, υποχρεωτικά πεδία)
  → NormalizationService → NormalizedDescription
  → build TransactionImportModel { Date, Amount, Type, Original, Normalized }
  → FingerprintService (+ occurrence ordinal)
  → DuplicateDetection (DB + in-file)
  → CategorySuggestionService
  → persist ImportBatch(Pending) + ImportStagingRow[]
  → Redirect σε Preview
```

- `ExcelReader` ξέρει **μόνο** πώς να διαβάζει κελιά με βάση το `ImportProfile` και να επιστρέφει
  ένα κοινό internal model. Καμία λογική τράπεζας hard-coded.
- **TransactionImportModel** (κοινό internal model):
  `Date`, `Amount` (θετικό), `Type`, `OriginalDescription`, `NormalizedDescription`.
- Validation errors → **δεν** γίνεται partial insert· ο χρήστης βλέπει σαφή λίστα σφαλμάτων ανά γραμμή (§13).

---

## 7. Duplicate detection strategy (§7)

**Fingerprint (deterministic, SHA-256):**

```
canonical = AccountId | yyyy-MM-dd(Date) | Amount(invariant, 2 decimals) | Type | NormalizedDescription
            [ | BankReference αν το ImportProfile δίνει μοναδικό reference ] | OccurrenceOrdinal
Fingerprint = SHA256Hex(canonical)
```

Κρίσιμο σημείο (§7 "Προσοχή"): δύο **γνήσιες** διαφορετικές κινήσεις μπορεί να έχουν ίδια
ημερομηνία/ποσό/περιγραφή.

- **Αν** το format έχει bank-provided μοναδικό reference → το χρησιμοποιούμε (ισχυρότερο, χωρίς ordinal).
- **Αλλιώς:** εισάγουμε **OccurrenceOrdinal**: μέσα σε κάθε ομάδα ίδιων `(Account,Date,Amount,Type,NormDesc)`,
  αριθμούμε ντετερμινιστικά 1,2,3… με σταθερή σειρά (σειρά γραμμών αρχείου). Έτσι:
  - 3 γνήσιες ίδιες κινήσεις της ίδιας μέρας → 3 διαφορετικά fingerprints (δεν χάνονται).
  - Re-import του ίδιου αρχείου → ίδια σειρά → ίδια fingerprints → αναγνωρίζονται ως duplicates.

**Δύο επίπεδα προστασίας:**
1. Application check: lookup fingerprint στη βάση **και** μέσα στο ίδιο batch πριν το insert.
2. **Unique index** `(AccountId, Fingerprint)`: αν κάτι ξεφύγει (π.χ. concurrent import),
   το insert αποτυγχάνει αντί να δημιουργήσει duplicate.

> Το ακριβές σετ πεδίων του fingerprint **οριστικοποιείται αφού δούμε πραγματικό Excel** — γι' αυτό
> τα components είναι configurable/επεκτάσιμα. Το ordinal είναι το βασικό safety net μέχρι τότε.

---

## 8. Category suggestion & learning (§9–§12)

**Σειρά προτεραιότητας** (πρώτο που "χτυπά" με αρκετή βεβαιότητα κερδίζει):

1. **User-defined rule** (`CategoryRule.Source=UserDefined`), κατά `Priority` desc → strong match.
2. **History:** πιο συχνό `CategoryId` προηγούμενων `Transactions` με ίδιο `NormalizedDescription`.
   Απαιτείται minimum support (π.χ. ≥ N ή dominant ποσοστό) αλλιώς δεν είναι αρκετά σίγουρο.
3. **Learned rule** (`Source=Learned`) / **normalized merchant match** (Contains/StartsWith).
4. (Επεκτάσιμο) άλλη λογική matching.
5. **Uncategorized** → εμφανίζεται *"Δεν βρέθηκε κατηγορία"*, ο χρήστης επιλέγει (§12).

> Αν καμία πηγή δεν περνά το threshold βεβαιότητας → **δεν** επιλέγουμε αυθαίρετα κατηγορία.

**Learning από user corrections (§11):** στο **Confirm**, για κάθε γραμμή όπου
`SelectedCategoryId != SuggestedCategoryId` (ή suggestion ήταν None), κάνουμε **upsert** σε
`CategoryRule` (`Pattern = NormalizedDescription`, `MatchType=Exact`, `Source=Learned`,
`CategoryId = selected`, `HitCount++`). Έτσι το επόμενο import προτείνει σωστά.

Το κάθε suggestion κρατά και `SuggestionSource` για διαφάνεια/debugging.

---

## 9. Description normalization (§10)

**Configurable pipeline** (μη-καταστροφικό — κρατάμε πάντα `OriginalDescription`):

1. Trim + collapse whitespace.
2. Uppercase (invariant).
3. Αφαίρεση trailing store numbers/κωδικών (π.χ. `SKLAVENITIS 1234` → `SKLAVENITIS`).
4. Αφαίρεση legal suffixes (`AE`, `ΑΕ`, `A.E.`, `LTD`, ...) — configurable λίστα.
5. (Optional/configurable) Greek↔Latin awareness ώστε `ΣΚΛΑΒΕΝΙΤΗΣ` ≈ `SKLAVENITIS`.

Οι κανόνες είναι configuration, όχι hard-coded, ώστε να μην "καταστρέφουν" σημαντική πληροφορία.
Το αποτέλεσμα → `NormalizedDescription` (χρησιμοποιείται σε fingerprint & matching).

---

## 10. Import preview workflow (§8, §13)

```
UPLOAD → READ → VALIDATE → NORMALIZE → CANDIDATES → CHECK DUPLICATES
       → SUGGEST → SHOW PREVIEW → USER EDITS → USER CONFIRMS
       → INSERT NEW ONLY → UPDATE RULES → COMPLETED
```

- **Preview page** διαβάζει από `ImportStagingRow`:

  | Date | Description | Amount | Suggested Category | Selected (dropdown) | Status |
  | --- | --- | --- | --- | --- | --- |
  | 05/09/2026 | SKLAVENITIS | 52.30 | SUPERMARKET → Sklavenitis | *(editable)* | New |
  | 07/09/2026 | SKLAVENITIS | 45.20 | SUPERMARKET → Sklavenitis | *(editable)* | Duplicate |

- Ο χρήστης αλλάζει `SelectedCategoryId` (cascading dropdown βάσει hierarchy). Duplicates
  εμφανίζονται αλλά **δεν** επιλέγονται για insert (by default).
- **Confirm** (μία DB transaction):
  1. Insert **μόνο** τα New (skip duplicates).
  2. Update learning rules (§8).
  3. `ImportBatch.Status = Completed`, γέμισμα `NewCount/DuplicateCount`.
  4. Καθαρισμός staging.
- **Cancel** → `Status=Cancelled`, τίποτα στα `Transactions`.
- Καμία κίνηση στη βάση **πριν** το confirm (§8).

---

## 11. Dashboard (§14–§16)

`DashboardService` με **date filter** (Today / This week / This month / Previous month / This year / Custom).

Μετρικά: Total income, Total expenses, Balance, #transactions, Average, Largest expenses,
Expenses by category (**hierarchical roll-up**), by month, by merchant.

**Hierarchical roll-up (§3, §15):** το ποσό ενός parent = άθροισμα όλων των **descendants** +
δικές του άμεσες κινήσεις. Επειδή depth ≤ 3, το κάνουμε με ένα in-memory roll-up πάνω στα
grouped-by-category sums (φθηνό, χωρίς heavy recursive SQL). Drill-down: άνοιγμα parent →
εμφάνιση children με τα ποσά τους.

---

## 12. Security (§18)

- **Authentication/Authorization** (Identity), όλα τα pages `[Authorize]`.
- **HTTPS redirect + HSTS**· antiforgery tokens σε όλα τα forms.
- **Secure DB connection** (`Encrypt=True`), connection string εκτός source control.
- **Input validation** (server-side) + model validation.
- **Safe Excel parsing:** ClosedXML (χωρίς macro execution), έλεγχος extension/content-type,
  όριο μεγέθους, όριο αριθμού rows, καθαρισμός uploaded temp files.
- **Ελάχιστο logging** — όχι λεπτομέρειες συναλλαγών/ποσών στα logs.
- **Καμία περιττή εξωτερική κλήση** — όλα τοπικά στο hosting.

---

## 13. Τι θέλω από εσένα πριν την υλοποίηση

1. **Δείγμα πραγματικού Excel** (1-2 τράπεζες, ανωνυμοποιημένο): για να οριστικοποιήσουμε
   column mapping (`ImportProfile`), date/decimal format, και το **ακριβές σετ πεδίων του fingerprint**.
2. Επιβεβαίωση: **Razor Pages** & **.NET 8** OK;
3. Επιβεβαίωση απόφασης **Amount(θετικό) + Type** αντί για signed amount.
4. Χρειάζεσαι multi-user από τώρα ή single-user (με Identity έτοιμο για επέκταση);

Μόλις συμφωνήσουμε, προχωράω στη σειρά της §20:
schema → entities → migration → import → duplicate → suggestion → preview → υλοποίηση.
