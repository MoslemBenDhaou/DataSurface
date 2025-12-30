namespace DataSurface.Core;

/// <summary>
/// Feature flags controlling which DataSurface capabilities are enabled.
/// </summary>
/// <remarks>
/// <para>
/// Use this class to selectively enable or disable features. You can either:
/// <list type="bullet">
/// <item>Use a preset via <see cref="Minimal"/>, <see cref="Standard"/>, or <see cref="Full"/></item>
/// <item>Configure individual features manually</item>
/// </list>
/// </para>
/// </remarks>
public sealed class DataSurfaceFeatures
{
    // ============ CORE CRUD ============

    /// <summary>
    /// Enable field-level validation (MinLength, MaxLength, Min, Max, Regex, AllowedValues).
    /// </summary>
    public bool EnableFieldValidation { get; set; } = true;

    /// <summary>
    /// Enable automatic application of default values on create.
    /// </summary>
    public bool EnableDefaultValues { get; set; } = true;

    /// <summary>
    /// Enable computed field evaluation at read time.
    /// </summary>
    public bool EnableComputedFields { get; set; } = false;

    /// <summary>
    /// Enable field projection via ?fields= query parameter.
    /// </summary>
    public bool EnableFieldProjection { get; set; } = false;

    // ============ SECURITY ============

    /// <summary>
    /// Enable tenant isolation via [CrudTenant] attribute.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = false;

    /// <summary>
    /// Enable row-level security via IResourceFilter.
    /// </summary>
    public bool EnableRowLevelSecurity { get; set; } = false;

    /// <summary>
    /// Enable resource-level authorization via IResourceAuthorizer.
    /// </summary>
    public bool EnableResourceAuthorization { get; set; } = false;

    /// <summary>
    /// Enable field-level authorization via IFieldAuthorizer.
    /// </summary>
    public bool EnableFieldAuthorization { get; set; } = false;

    // ============ OBSERVABILITY ============

    /// <summary>
    /// Enable audit logging via IAuditLogger.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = false;

    /// <summary>
    /// Enable OpenTelemetry metrics via DataSurfaceMetrics.
    /// </summary>
    public bool EnableMetrics { get; set; } = false;

    /// <summary>
    /// Enable distributed tracing via DataSurfaceTracing.
    /// </summary>
    public bool EnableTracing { get; set; } = false;

    // ============ CACHING ============

    /// <summary>
    /// Enable query result caching via IQueryResultCache.
    /// </summary>
    public bool EnableQueryCaching { get; set; } = false;

    // ============ HOOKS & OVERRIDES ============

    /// <summary>
    /// Enable lifecycle hooks via ICrudHook and ICrudHook&lt;T&gt;.
    /// </summary>
    public bool EnableHooks { get; set; } = false;

    /// <summary>
    /// Enable CRUD operation overrides via CrudOverrideRegistry.
    /// </summary>
    public bool EnableOverrides { get; set; } = false;

    // ============ INTEGRATION ============

    /// <summary>
    /// Enable webhook publishing via IWebhookPublisher.
    /// </summary>
    public bool EnableWebhooks { get; set; } = false;

    // ============ PRESETS ============

    /// <summary>
    /// Minimal preset: Core CRUD only, no security, observability, or advanced features.
    /// </summary>
    public static DataSurfaceFeatures Minimal => new()
    {
        EnableFieldValidation = true,
        EnableDefaultValues = true,
        EnableComputedFields = false,
        EnableFieldProjection = false,
        EnableTenantIsolation = false,
        EnableRowLevelSecurity = false,
        EnableResourceAuthorization = false,
        EnableFieldAuthorization = false,
        EnableAuditLogging = false,
        EnableMetrics = false,
        EnableTracing = false,
        EnableQueryCaching = false,
        EnableHooks = false,
        EnableOverrides = false,
        EnableWebhooks = false
    };

    /// <summary>
    /// Standard preset: Core CRUD with security and basic observability.
    /// </summary>
    public static DataSurfaceFeatures Standard => new()
    {
        EnableFieldValidation = true,
        EnableDefaultValues = true,
        EnableComputedFields = true,
        EnableFieldProjection = true,
        EnableTenantIsolation = true,
        EnableRowLevelSecurity = true,
        EnableResourceAuthorization = true,
        EnableFieldAuthorization = true,
        EnableAuditLogging = true,
        EnableMetrics = true,
        EnableTracing = true,
        EnableQueryCaching = true,
        EnableHooks = true,
        EnableOverrides = true,
        EnableWebhooks = false
    };

    /// <summary>
    /// Full preset: All features enabled including webhooks.
    /// </summary>
    public static DataSurfaceFeatures Full => new()
    {
        EnableFieldValidation = true,
        EnableDefaultValues = true,
        EnableComputedFields = true,
        EnableFieldProjection = true,
        EnableTenantIsolation = true,
        EnableRowLevelSecurity = true,
        EnableResourceAuthorization = true,
        EnableFieldAuthorization = true,
        EnableAuditLogging = true,
        EnableMetrics = true,
        EnableTracing = true,
        EnableQueryCaching = true,
        EnableHooks = true,
        EnableOverrides = true,
        EnableWebhooks = true
    };

    /// <summary>
    /// Creates a new instance with minimal features enabled by default (field validation and default values only).
    /// Use <see cref="Standard"/> or <see cref="Full"/> presets for more features.
    /// </summary>
    public DataSurfaceFeatures() { }

    /// <summary>
    /// Creates a clone of the specified features configuration.
    /// </summary>
    public DataSurfaceFeatures(DataSurfaceFeatures other)
    {
        EnableFieldValidation = other.EnableFieldValidation;
        EnableDefaultValues = other.EnableDefaultValues;
        EnableComputedFields = other.EnableComputedFields;
        EnableFieldProjection = other.EnableFieldProjection;
        EnableTenantIsolation = other.EnableTenantIsolation;
        EnableRowLevelSecurity = other.EnableRowLevelSecurity;
        EnableResourceAuthorization = other.EnableResourceAuthorization;
        EnableFieldAuthorization = other.EnableFieldAuthorization;
        EnableAuditLogging = other.EnableAuditLogging;
        EnableMetrics = other.EnableMetrics;
        EnableTracing = other.EnableTracing;
        EnableQueryCaching = other.EnableQueryCaching;
        EnableHooks = other.EnableHooks;
        EnableOverrides = other.EnableOverrides;
        EnableWebhooks = other.EnableWebhooks;
    }
}
