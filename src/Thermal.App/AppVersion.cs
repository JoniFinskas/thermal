using System.Reflection;
namespace Thermal.App;
internal static class AppVersion {
    public static string Current=>typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??"Unknown";
}
