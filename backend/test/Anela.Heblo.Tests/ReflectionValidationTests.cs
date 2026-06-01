using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Anela.Heblo.Tests;

public class ReflectionValidationTests
{
    public static IEnumerable<object[]> GetClassesImplementingIClassFixture()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var allTypes = assembly.GetTypes();
        var classFixtureType = typeof(IClassFixture<>);

        foreach (var type in allTypes)
        {
            // Skip abstract classes and interfaces
            if (type.IsAbstract || type.IsInterface)
                continue;

            var interfaces = type.GetInterfaces();

            foreach (var interfaceType in interfaces)
            {
                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == classFixtureType)
                {
                    var genericArgument = interfaceType.GetGenericArguments()[0];
                    yield return new object[] { type, interfaceType, genericArgument };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetClassesImplementingIClassFixture))]
    public void Class_Should_Not_ImplementIClassFixtureWithWebApplicationFactory(
        Type classType,
        Type interfaceType,
        Type genericArgument)
    {
        // Arrange
        var webApplicationFactoryType = typeof(WebApplicationFactory<>);

        // Act
        var isWebApplicationFactory = genericArgument.IsGenericType &&
                                     genericArgument.GetGenericTypeDefinition() == webApplicationFactoryType;

        // Assert
        Assert.False(isWebApplicationFactory,
            $"Class '{classType.FullName}' implements {interfaceType.Name} with WebApplicationFactory " +
            $"(Generic argument: {genericArgument.Name}), which is not allowed. " +
            $"Use HebloWebApplicationFactory instead.");
    }
}