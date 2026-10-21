namespace Thermal.App;
// Acer access requires a deliberate Settings action for the current session.
internal static class BuildPolicy {
    public static bool PublicBuild=>true;
    public static bool RequestAcerOnOpen=>false;
}
