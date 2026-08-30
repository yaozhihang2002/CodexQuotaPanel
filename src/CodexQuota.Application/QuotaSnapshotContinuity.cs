using CodexQuota.Domain;

namespace CodexQuota.Application;

public enum QuotaSnapshotSelectionKind
{
    None,
    Live,
    Retained,
    Local
}

public sealed record QuotaSnapshotSelection(
    OfficialQuotaSnapshot? Snapshot,
    QuotaSnapshotSelectionKind Kind)
{
    public bool IsFresh => Kind is QuotaSnapshotSelectionKind.Live or QuotaSnapshotSelectionKind.Local;
}

public static class QuotaSnapshotContinuity
{
    public static QuotaSnapshotSelection Select(
        OfficialQuotaSnapshot? live,
        OfficialQuotaSnapshot? retained,
        OfficialQuotaSnapshot? local)
    {
        if (live is not null)
            return new QuotaSnapshotSelection(live, QuotaSnapshotSelectionKind.Live);
        if (retained is not null)
            return new QuotaSnapshotSelection(retained, QuotaSnapshotSelectionKind.Retained);
        if (local is not null)
            return new QuotaSnapshotSelection(local, QuotaSnapshotSelectionKind.Local);
        return new QuotaSnapshotSelection(null, QuotaSnapshotSelectionKind.None);
    }
}
