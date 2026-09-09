namespace MoneyTracker.Domain;

/// <summary>Tracks one Excel import (spec §13).</summary>
public class ImportBatch
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int? ImportProfileId { get; set; }
    public ImportProfile? ImportProfile { get; set; }

    public string FileName { get; set; } = string.Empty;

    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    public int TotalRows { get; set; }
    public int NewCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DuplicateCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<ImportStagingRow> StagingRows { get; set; } = new List<ImportStagingRow>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
