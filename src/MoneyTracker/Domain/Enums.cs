namespace MoneyTracker.Domain;

public enum TransactionType : byte
{
    Expense = 1,
    Income = 2
}

public enum MatchType : byte
{
    Exact = 1,
    Contains = 2,
    StartsWith = 3
}

public enum RuleSource : byte
{
    UserDefined = 1,
    Learned = 2
}

public enum ImportStatus : byte
{
    Pending = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}

/// <summary>How a suggested category was produced (for transparency/debugging).</summary>
public enum SuggestionSource : byte
{
    None = 0,
    UserRule = 1,
    History = 2,
    LearnedRule = 3,
    BankCategory = 4
}
