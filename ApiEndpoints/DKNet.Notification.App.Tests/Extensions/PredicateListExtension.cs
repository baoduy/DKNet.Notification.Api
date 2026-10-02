using System.Runtime.CompilerServices;
using NetArchTest.Rules;

namespace DKNet.Notification.App.Tests.Extensions;

internal static class PredicateListExtension
{
    public static PredicateList AndNotSystemGeneratedClasses(this PredicateList predicates)
    {
        return predicates
            .And().DoNotHaveNameMatching(@".*<.*>.*") // Exclude any type with angle brackets
            .And().DoNotHaveNameMatching(@"^<>.*") // Exclude display/closure classes
            .And().DoNotHaveCustomAttribute(typeof(CompilerGeneratedAttribute));
    }
}