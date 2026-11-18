using System.Reflection;
namespace SentoBar;
internal static class AppVersion
{
    internal static string Display=>typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??typeof(App).Assembly.GetName().Version!.ToString(3);
}
