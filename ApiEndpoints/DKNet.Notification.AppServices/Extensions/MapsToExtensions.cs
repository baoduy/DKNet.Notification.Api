using System.Reflection;
using DKNet.EfCore.DtoGenerator;
using DKNet.Fw.Extensions.TypeExtractors;

namespace DKNet.Notification.AppServices.Extensions;

internal static class MapsToExtensions
{
    #region Methods

    public static void ScanMaps(this TypeAdapterConfig config)
    {
        var mapsToTypes = typeof(MapsToExtensions).Assembly
            .Extract().Classes().NotAbstract().NotGeneric()
            .Where(t => t.GetCustomAttribute<MapsFromAttribute>() != null ||
                        t.GetCustomAttribute<GenerateDtoAttribute>() != null);

        foreach (var type in mapsToTypes)
        {
            var mapsFromAttr = type.GetCustomAttribute<MapsFromAttribute>();
            if (mapsFromAttr is not null)
            {
                config.NewConfig(mapsFromAttr.EntityType, type);
                continue;
            }

            var generateDtoAtt = type.GetCustomAttribute<GenerateDtoAttribute>();
            if (generateDtoAtt is not null)
            {
                config.NewConfig(generateDtoAtt.EntityType, type);
            }
        }

        // Must run after the loop above: an IRegister customisation (e.g. ProductMappingRegister)
        // merges onto a NewConfig'd pair via ForType — running it first would have its merge wiped out
        // by the convention's NewConfig call.
        config.Scan(typeof(MapsToExtensions).Assembly);
    }

    #endregion
}