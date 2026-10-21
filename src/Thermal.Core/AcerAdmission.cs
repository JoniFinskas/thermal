namespace Thermal.Core;
public static class AcerAdmission {
    // A code-reviewed admission change is required. No preferences, JSON, environment flag or CLI can enable live access.
    public static bool LiveAllowed=>false;
    public const string Pending="Acer firmware readings are awaiting safety qualification. No Windows access is requested.";
}
