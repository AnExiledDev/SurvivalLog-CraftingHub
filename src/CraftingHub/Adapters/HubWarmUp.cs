using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CraftingHub.Core;

namespace CraftingHub.Adapters;

/// <summary>
/// The first workbench open of a session used to compile the hub's code on the game's frame: reading the bench,
/// planning every recipe for its marks, costing the queue and building the panel. At load, on a worker, the plain
/// part is run once on a made-up bench and the rest is compiled without being run, so nothing here touches the game.
/// A failure only costs the first open its head start, and is noted in the mod's file only: the BepInEx console is
/// left to the game's thread.
/// </summary>
internal static class HubWarmUp
{
    private const string CoreNamespace = "CraftingHub.Core";

    private static readonly Type[] Adapters =
    {
        typeof(HubSession), typeof(Workbench), typeof(WorkbenchPage), typeof(HomeStock), typeof(StoragePool), typeof(RecipeRefresh),
        typeof(PageMessages), typeof(KeepStore), typeof(CraftTicker),
    };

    public static void Start(Action<string> logFileOnly) =>
        Task.Run(() =>
        {
            try
            {
                CraftWarmUp.Run();

                foreach (var type in typeof(CraftWarmUp).Assembly.GetTypes())
                {
                    if (type.Namespace == CoreNamespace) Prepare(type);
                }

                foreach (var type in Adapters)
                {
                    Prepare(type);
                    PrepareNested(type);
                }
            }
            catch (Exception ex)
            {
                logFileOnly("warm-up failed, the first workbench open may take a moment longer: " + ex.Message);
            }
        });

    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Compiles every method the type declares without running any. Nested types are listed on their own.</summary>
    private static void Prepare(Type type)
    {
        if (type.ContainsGenericParameters) return;

        foreach (var method in type.GetMethods(Declared))
        {
            if (!method.IsAbstract && !method.ContainsGenericParameters) RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }
    }

    /// <summary>An adapter's lambdas and records live in nested types, which the Core pass lists itself.</summary>
    private static void PrepareNested(Type type)
    {
        foreach (var nested in type.GetNestedTypes(Declared))
        {
            Prepare(nested);
            PrepareNested(nested);
        }
    }
}
