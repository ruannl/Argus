using argus.Server.Data;

namespace argus.Server.Http;

/// <summary>
/// Customer-scoped finance HTTP surface. Every handler proves the trusted
/// frontend caller, then independently authorizes the acting user's session,
/// then scopes every store call by that customer id. User-defined names never
/// reach a log line, an audit event, or a URL segment.
/// </summary>
public sealed class FinanceApi(FinanceGuard guard, FinanceStore financeStore, ILoggerFactory loggerFactory)
{
    private readonly ILogger _audit = loggerFactory.CreateLogger("audit");
    private readonly ILogger _logger = loggerFactory.CreateLogger("finance");

    public void Map(WebApplication app)
    {
        app.MapGet("/v1/finance/accounts", ListAccountsAsync);
        app.MapPost("/v1/finance/accounts", CreateAccountAsync);
        app.MapPatch("/v1/finance/accounts/{id:guid}", UpdateAccountAsync);
        app.MapPost("/v1/finance/accounts/{id:guid}/archive", ArchiveAccountAsync);
        app.MapPost("/v1/finance/accounts/{id:guid}/restore", RestoreAccountAsync);

        app.MapGet("/v1/finance/categories", ListCategoriesAsync);
        app.MapPost("/v1/finance/categories", CreateCategoryAsync);
        app.MapPatch("/v1/finance/categories/{id:guid}", UpdateCategoryAsync);
        app.MapPost("/v1/finance/categories/{id:guid}/archive", ArchiveCategoryAsync);
        app.MapPost("/v1/finance/categories/{id:guid}/restore", RestoreCategoryAsync);
        app.MapDelete("/v1/finance/categories/{id:guid}", DeleteCategoryAsync);

        app.MapGet("/v1/finance/merchant-tags", ListMerchantTagsAsync);
        app.MapPost("/v1/finance/merchant-tags", CreateMerchantTagAsync);
        app.MapPatch("/v1/finance/merchant-tags/{id:guid}", UpdateMerchantTagAsync);
        app.MapPost("/v1/finance/merchant-tags/{id:guid}/archive", ArchiveMerchantTagAsync);
        app.MapPost("/v1/finance/merchant-tags/{id:guid}/restore", RestoreMerchantTagAsync);
        app.MapDelete("/v1/finance/merchant-tags/{id:guid}", DeleteMerchantTagAsync);

        app.MapGet("/v1/finance/merchant-aliases", ListMerchantAliasesAsync);
        app.MapPost("/v1/finance/merchant-aliases", CreateMerchantAliasAsync);
        app.MapPatch("/v1/finance/merchant-aliases/{id:guid}", UpdateMerchantAliasAsync);
        app.MapDelete("/v1/finance/merchant-aliases/{id:guid}", DeleteMerchantAliasAsync);
    }

    // --- Guard -------------------------------------------------------------

    private Task<(Guid? CustomerId, IResult? Failure)> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken) =>
        guard.AuthenticateAsync(context, cancellationToken);

    private Task<IResult> GuardedAsync(Func<Task<IResult>> action, string operation) =>
        FinanceGuard.GuardedAsync(action, operation, _logger);

    // --- Financial accounts ------------------------------------------------

    private Task<IResult> ListAccountsAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var accounts = await financeStore.ListAccountsAsync(customerId!.Value, cancellationToken);
            return Results.Json(new { accounts = accounts.Select(FinanceAccountResponse.From).ToArray(), });
        }, "accounts_list");

    private Task<IResult> CreateAccountAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var parsed = await JsonBody.ReadAsync<CreateAccountRequest>(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.CreateAccountAsync(customerId!.Value, parsed.Value!.DisplayName, parsed.Value.Institution, cancellationToken);

            switch (outcome)
            {
                case AccountOutcome.Success success:
                    _audit.LogInformation("audit.finance_account_created customer_id={CustomerId} account_id={AccountId}", customerId.Value, success.Account.Id);
                    return Results.Json(FinanceAccountResponse.From(success.Account), statusCode: 201);
                case AccountOutcome.InvalidName:
                    return Errors.InvalidRequest();
                case AccountOutcome.NotFound:
                    return Errors.NotFound();
                default:
                    return Errors.Internal();
            }
        }, "accounts_create");

    private Task<IResult> UpdateAccountAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var parsed = await JsonBody.ReadAccountPatchAsync(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.UpdateAccountAsync(customerId!.Value, id.Value, parsed.DisplayName, parsed.InstitutionProvided, parsed.Institution, cancellationToken);

            switch (outcome)
            {
                case AccountOutcome.Success success:
                    _audit.LogInformation("audit.finance_account_updated customer_id={CustomerId} account_id={AccountId}", customerId.Value, success.Account.Id);
                    return Results.Json(FinanceAccountResponse.From(success.Account));
                case AccountOutcome.InvalidName:
                    return Errors.InvalidRequest();
                case AccountOutcome.NotFound:
                    return Errors.NotFound();
                default:
                    return Errors.Internal();
            }
        }, "accounts_update");

    private Task<IResult> ArchiveAccountAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetAccountArchivedAsync(context, cancellationToken, archived: true);

    private Task<IResult> RestoreAccountAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetAccountArchivedAsync(context, cancellationToken, archived: false);

    private Task<IResult> SetAccountArchivedAsync(HttpContext context, CancellationToken cancellationToken, bool archived) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var outcome = await financeStore.SetAccountArchivedAsync(customerId!.Value, id.Value, archived, cancellationToken);

            switch (outcome)
            {
                case AccountOutcome.Success success:
                    _audit.LogInformation("audit.finance_account_archived customer_id={CustomerId} account_id={AccountId} archived={Archived}", customerId.Value, success.Account.Id, archived);
                    return Results.Json(FinanceAccountResponse.From(success.Account));
                case AccountOutcome.NotFound:
                    return Errors.NotFound();
                case AccountOutcome.InvalidName:
                    return Errors.InvalidRequest();
                default:
                    return Errors.Internal();
            }
        }, archived ? "accounts_archive" : "accounts_restore");

    // --- Categories --------------------------------------------------------

    private Task<IResult> ListCategoriesAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var categories = await financeStore.ListCategoriesAsync(customerId!.Value, cancellationToken);
            return Results.Json(new { categories = categories.Select(FinanceCategoryResponse.From).ToArray(), });
        }, "categories_list");

    private Task<IResult> CreateCategoryAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var parsed = await JsonBody.ReadAsync<CreateCategoryRequest>(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.CreateCategoryAsync(customerId!.Value, parsed.Value!.DisplayName, parsed.Value.Kind, cancellationToken);

            return CategoryResult(outcome, customerId.Value, "audit.finance_category_created", created: true);
        }, "categories_create");

    private Task<IResult> UpdateCategoryAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var parsed = await JsonBody.ReadAsync<UpdateCategoryRequest>(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.UpdateCategoryAsync(customerId!.Value, id.Value, parsed.Value!.DisplayName, parsed.Value.Kind, parsed.Value.ConfirmKindChange ?? false, cancellationToken);

            return CategoryResult(outcome, customerId.Value, "audit.finance_category_updated", created: false);
        }, "categories_update");

    private Task<IResult> ArchiveCategoryAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetCategoryArchivedAsync(context, cancellationToken, archived: true);

    private Task<IResult> RestoreCategoryAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetCategoryArchivedAsync(context, cancellationToken, archived: false);

    private Task<IResult> SetCategoryArchivedAsync(HttpContext context, CancellationToken cancellationToken, bool archived) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var outcome = await financeStore.SetCategoryArchivedAsync(customerId!.Value, id.Value, archived, cancellationToken);

            return CategoryResult(outcome, customerId.Value, archived ? "audit.finance_category_archived" : "audit.finance_category_restored", created: false);
        }, archived ? "categories_archive" : "categories_restore");

    private Task<IResult> DeleteCategoryAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var outcome = await financeStore.DeleteCategoryAsync(customerId!.Value, id.Value, cancellationToken);

            switch (outcome)
            {
                case CategoryDeleteOutcome.Success success:
                    _audit.LogInformation("audit.finance_category_deleted customer_id={CustomerId} category_id={CategoryId} reassigned_count={ReassignedCount}", customerId.Value, id.Value, success.ReassignedTransactionCount);
                    return Results.Json(new { deleted = true, reassigned_transaction_count = success.ReassignedTransactionCount, uncategorised_category_id = success.UncategorisedCategoryId, });
                case CategoryDeleteOutcome.SystemCategoryImmutable:
                    return Errors.SystemCategoryImmutable();
                case CategoryDeleteOutcome.NotFound:
                    return Errors.NotFound();
                default:
                    return Errors.Internal();
            }
        }, "categories_delete");

    private IResult CategoryResult(CategoryOutcome outcome,
                                   Guid customerId,
                                   string auditEvent,
                                   bool created)
    {
        switch (outcome)
        {
            case CategoryOutcome.Success success:
                _audit.LogInformation("{AuditEvent} customer_id={CustomerId} category_id={CategoryId} kind={Kind}", auditEvent, customerId, success.Category.Id, success.Category.Kind);
                return Results.Json(FinanceCategoryResponse.From(success.Category), statusCode: created ? 201 : 200);
            case CategoryOutcome.InvalidName:
            case CategoryOutcome.InvalidKind:
                return Errors.InvalidRequest();
            case CategoryOutcome.DuplicateName:
                return Errors.DuplicateName();
            case CategoryOutcome.SystemCategoryImmutable:
                return Errors.SystemCategoryImmutable();
            case CategoryOutcome.KindChangeConfirmationRequired:
                return Errors.KindChangeConfirmationRequired();
            case CategoryOutcome.NotFound:
                return Errors.NotFound();
            default:
                return Errors.Internal();
        }
    }

    // --- Merchant tags -----------------------------------------------------

    private Task<IResult> ListMerchantTagsAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var tags = await financeStore.ListMerchantTagsAsync(customerId!.Value, cancellationToken);
            return Results.Json(new { merchant_tags = tags.Select(FinanceMerchantTagResponse.From).ToArray(), });
        }, "merchant_tags_list");

    private Task<IResult> CreateMerchantTagAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var parsed = await JsonBody.ReadAsync<CreateMerchantTagRequest>(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.CreateMerchantTagAsync(customerId!.Value, parsed.Value!.DisplayName, cancellationToken);

            return MerchantTagResult(outcome, customerId.Value, "audit.finance_merchant_tag_created", created: true);
        }, "merchant_tags_create");

    private Task<IResult> UpdateMerchantTagAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var parsed = await JsonBody.ReadAsync<UpdateMerchantTagRequest>(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.UpdateMerchantTagAsync(customerId!.Value, id.Value, parsed.Value!.DisplayName, cancellationToken);

            return MerchantTagResult(outcome, customerId.Value, "audit.finance_merchant_tag_updated", created: false);
        }, "merchant_tags_update");

    private Task<IResult> ArchiveMerchantTagAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetMerchantTagArchivedAsync(context, cancellationToken, archived: true);

    private Task<IResult> RestoreMerchantTagAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetMerchantTagArchivedAsync(context, cancellationToken, archived: false);

    private Task<IResult> SetMerchantTagArchivedAsync(HttpContext context, CancellationToken cancellationToken, bool archived) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var outcome = await financeStore.SetMerchantTagArchivedAsync(customerId!.Value, id.Value, archived, cancellationToken);

            return MerchantTagResult(outcome, customerId.Value, archived ? "audit.finance_merchant_tag_archived" : "audit.finance_merchant_tag_restored", created: false);
        }, archived ? "merchant_tags_archive" : "merchant_tags_restore");

    private Task<IResult> DeleteMerchantTagAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var outcome = await financeStore.DeleteMerchantTagAsync(customerId!.Value, id.Value, cancellationToken);

            switch (outcome)
            {
                case MerchantTagDeleteOutcome.Success success:
                    _audit.LogInformation("audit.finance_merchant_tag_deleted customer_id={CustomerId} merchant_tag_id={MerchantTagId} unassigned_count={UnassignedCount}", customerId.Value, id.Value, success.UnassignedTransactionCount);
                    return Results.Json(new { deleted = true, unassigned_transaction_count = success.UnassignedTransactionCount, });
                case MerchantTagDeleteOutcome.NotFound:
                    return Errors.NotFound();
                default:
                    return Errors.Internal();
            }
        }, "merchant_tags_delete");

    // --- Merchant aliases --------------------------------------------------

    private Task<IResult> ListMerchantAliasesAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var aliases = await financeStore.ListMerchantAliasesAsync(customerId!.Value, cancellationToken);
            return Results.Json(new { merchant_aliases = aliases.Select(FinanceMerchantAliasResponse.From).ToArray(), });
        }, "merchant_aliases_list");

    private Task<IResult> CreateMerchantAliasAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var parsed = await JsonBody.ReadAsync<CreateMerchantAliasRequest>(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.CreateMerchantAliasAsync(
                customerId!.Value,
                parsed.Value!.DisplayAlias,
                parsed.Value.MerchantTagId,
                parsed.Value.CategoryId,
                cancellationToken);

            return MerchantAliasResult(outcome, customerId.Value, "audit.finance_merchant_alias_created", created: true);
        }, "merchant_aliases_create");

    private Task<IResult> UpdateMerchantAliasAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var parsed = await JsonBody.ReadAsync<UpdateMerchantAliasRequest>(context.Request, cancellationToken);
            if (!parsed.Parsed)
            {
                return Errors.InvalidRequest();
            }

            var outcome = await financeStore.UpdateMerchantAliasAsync(
                customerId!.Value,
                id.Value,
                parsed.Value!.DisplayAlias,
                parsed.Value.MerchantTagId,
                parsed.Value.CategoryId,
                cancellationToken);

            return MerchantAliasResult(outcome, customerId.Value, "audit.finance_merchant_alias_updated", created: false);
        }, "merchant_aliases_update");

    private Task<IResult> DeleteMerchantAliasAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            bool deleted = await financeStore.DeleteMerchantAliasAsync(customerId!.Value, id.Value, cancellationToken);
            if (!deleted)
            {
                return Errors.NotFound();
            }

            _audit.LogInformation("audit.finance_merchant_alias_deleted customer_id={CustomerId} alias_id={AliasId}", customerId.Value, id.Value);
            return Results.Json(new { deleted = true, });
        }, "merchant_aliases_delete");

    private IResult MerchantAliasResult(MerchantAliasOutcome outcome,
                                        Guid customerId,
                                        string auditEvent,
                                        bool created)
    {
        switch (outcome)
        {
            case MerchantAliasOutcome.Success success:
                _audit.LogInformation("{AuditEvent} customer_id={CustomerId} alias_id={AliasId}", auditEvent, customerId, success.Alias.Id);
                return Results.Json(FinanceMerchantAliasResponse.From(success.Alias), statusCode: created ? 201 : 200);
            case MerchantAliasOutcome.InvalidName:
                return Errors.InvalidRequest();
            case MerchantAliasOutcome.DuplicateName:
                return Errors.DuplicateName();
            case MerchantAliasOutcome.LimitReached:
                return Errors.InvalidRequest();
            case MerchantAliasOutcome.TagNotFound:
            case MerchantAliasOutcome.CategoryNotFound:
            case MerchantAliasOutcome.NotFound:
                return Errors.NotFound();
            default:
                return Errors.Internal();
        }
    }

    private IResult MerchantTagResult(MerchantTagOutcome outcome,
                                      Guid customerId,
                                      string auditEvent,
                                      bool created)
    {
        switch (outcome)
        {
            case MerchantTagOutcome.Success success:
                _audit.LogInformation("{AuditEvent} customer_id={CustomerId} merchant_tag_id={MerchantTagId}", auditEvent, customerId, success.Tag.Id);
                return Results.Json(FinanceMerchantTagResponse.From(success.Tag), statusCode: created ? 201 : 200);
            case MerchantTagOutcome.InvalidName:
                return Errors.InvalidRequest();
            case MerchantTagOutcome.DuplicateName:
                return Errors.DuplicateName();
            case MerchantTagOutcome.NotFound:
                return Errors.NotFound();
            default:
                return Errors.Internal();
        }
    }

    // --- Helpers -----------------------------------------------------------

    private static Guid? RouteGuid(HttpContext context) =>
        context.Request.RouteValues.TryGetValue("id", out object? value) &&
        Guid.TryParse(value?.ToString(), out var id)
            ? id
            : null;
}

public sealed record FinanceAccountResponse(Guid Id,
                                            string DisplayName,
                                            string? Institution,
                                            string Currency,
                                            bool IsArchived,
                                            long TransactionCount,
                                            string CreatedAt,
                                            string UpdatedAt)
{
    public static FinanceAccountResponse From(FinancialAccountRecord account) => new(
        account.Id,
        account.DisplayName,
        account.Institution,
        account.Currency,
        account.IsArchived,
        account.TransactionCount,
        Timestamps.Format(account.CreatedAt),
        Timestamps.Format(account.UpdatedAt));
}

public sealed record FinanceCategoryResponse(Guid Id,
                                             string DisplayName,
                                             string Kind,
                                             bool IsSystem,
                                             bool IsDefault,
                                             bool IsArchived,
                                             long TransactionCount,
                                             string CreatedAt,
                                             string UpdatedAt)
{
    public static FinanceCategoryResponse From(CategoryRecord category) => new(
        category.Id,
        category.DisplayName,
        category.Kind,
        category.IsSystem,
        category.IsDefault,
        category.IsArchived,
        category.TransactionCount,
        Timestamps.Format(category.CreatedAt),
        Timestamps.Format(category.UpdatedAt));
}

public sealed record FinanceMerchantAliasResponse(Guid Id,
                                                   string DisplayAlias,
                                                   Guid MerchantTagId,
                                                   string TagDisplayName,
                                                   Guid? CategoryId,
                                                   string? CategoryDisplayName,
                                                   DateTime CreatedAt,
                                                   DateTime UpdatedAt)
{
    public static FinanceMerchantAliasResponse From(MerchantAliasRecord alias) => new(
        alias.Id,
        alias.DisplayAlias,
        alias.MerchantTagId,
        alias.TagDisplayName,
        alias.CategoryId,
        alias.CategoryDisplayName,
        alias.CreatedAt,
        alias.UpdatedAt);
}

public sealed record FinanceMerchantTagResponse(Guid Id,
                                                string DisplayName,
                                                bool IsArchived,
                                                long TransactionCount,
                                                string CreatedAt,
                                                string UpdatedAt)
{
    public static FinanceMerchantTagResponse From(MerchantTagRecord tag) => new(
        tag.Id,
        tag.DisplayName,
        tag.IsArchived,
        tag.TransactionCount,
        Timestamps.Format(tag.CreatedAt),
        Timestamps.Format(tag.UpdatedAt));
}
