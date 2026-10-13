using FitCore.Platform.Modules;

namespace FitCore.Modules;

/// <summary>
/// Every backend module, in the order of ADR 0003 decision 2. Compiled into both hosts (FitCore.Web and
/// FitCore.Worker) so the app and the worker always load the same modules.
/// </summary>
internal static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new Identity.IdentityModule(),
        new Catalog.CatalogModule(),
        new Nutrition.NutritionModule(),
        new Training.TrainingModule(),
        new Progress.ProgressModule(),
        new Targets.TargetsModule(),
        new Care.CareModule(),
        new Sync.SyncModule(),
        new Billing.BillingModule(),
        new Integrations.IntegrationsModule(),
        new DataRights.DataRightsModule(),
        new Audit.AuditModule(),
    ];
}
