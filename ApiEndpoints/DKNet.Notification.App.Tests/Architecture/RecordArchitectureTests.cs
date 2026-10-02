using System.Reflection;
using DKNet.Notification.AppServices;
using DKNet.Notification.Domains.Share;

namespace DKNet.Notification.App.Tests.Architecture;

public class RecordArchitectureTests
{
    [Fact]
    public void RecordTypes_ShouldNotContain_PrivateSetters_OnPublicProperties()
    {
        // Assemblies to scan: Domain + AppServices
        var assemblies = new[] { typeof(DomainEntity).Assembly, typeof(AppSetup).Assembly };
        var failing = new List<string>();

        foreach (var asm in assemblies)
        {
            var recordTypes = asm.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && HasEqualityContract(t))
                .ToList();

            foreach (var type in recordTypes)
            {
                var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                foreach (var prop in props)
                {
                    var setMethod = prop.SetMethod;
                    if (setMethod != null && setMethod.IsPrivate) failing.Add($"{type.FullName}.{prop.Name}");
                }
            }
        }

        failing.ShouldBeEmpty(
            "Record types should not define public properties with private setters because AutoMapper cannot map them. Offenders: " +
            string.Join(", ", failing));
    }

    private static bool HasEqualityContract(Type t)
    {
        return t.GetProperty("EqualityContract",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null;
    }
}