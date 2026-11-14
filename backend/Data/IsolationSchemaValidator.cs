using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// Read-only validation of the isolation contract. It is used after migration
/// as the owner and on normal startup as the restricted runtime login. It
/// checks effective catalog state rather than trusting object names.
/// </summary>
public static class IsolationSchemaValidator
{
    private const string RuntimePolicyUsing =
        "(customer_id = (SELECT argus_current_customer_id() AS argus_current_customer_id))";
    private const string RuntimePolicyWithCheck =
        "COALESCE((customer_id = (SELECT argus_current_customer_id() AS argus_current_customer_id)), false)";

    public static async Task ValidateAsync(ArgusDbContext context,
                                           bool requireRuntimeLogin,
                                           CancellationToken cancellationToken)
    {
        string schema = await RequireCurrentSchemaAsync(context, cancellationToken);
        RoleRow[] roles = await ReadRolesAsync(context, cancellationToken);
        RoleRow owner = RequireRole(roles, DatabaseIsolationContract.OwnerRole);
        RoleRow migrator = RequireRole(roles, DatabaseIsolationContract.MigratorRole);
        RoleRow runtime = RequireRole(roles, DatabaseIsolationContract.RuntimeRole);

        Require(!owner.CanLogin, $"{DatabaseIsolationContract.OwnerRole} must not be a login role");
        Require(!owner.IsSuperuser && !owner.BypassRls, $"{DatabaseIsolationContract.OwnerRole} must not bypass RLS");
        Require(migrator.CanLogin, $"{DatabaseIsolationContract.MigratorRole} must be a login role");
        Require(!migrator.IsSuperuser && !migrator.BypassRls, $"{DatabaseIsolationContract.MigratorRole} must not bypass RLS");
        Require(!migrator.Inherit, $"{DatabaseIsolationContract.MigratorRole} must be NOINHERIT");
        Require(runtime.CanLogin, $"{DatabaseIsolationContract.RuntimeRole} must be a login role");
        Require(!runtime.IsSuperuser && !runtime.BypassRls, $"{DatabaseIsolationContract.RuntimeRole} must not bypass RLS");
        Require(!runtime.Inherit, $"{DatabaseIsolationContract.RuntimeRole} must be NOINHERIT");
        Require(!runtime.CreateDb && !runtime.CreateRole, $"{DatabaseIsolationContract.RuntimeRole} must not create databases or roles");

        MembershipRow[] memberships = await ReadMembershipsAsync(context, cancellationToken);
        Require(memberships.Any(item =>
                string.Equals(item.Member, DatabaseIsolationContract.MigratorRole, StringComparison.Ordinal) &&
                string.Equals(item.Role, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal) &&
                item.SetOption),
            $"{DatabaseIsolationContract.MigratorRole} must be allowed to SET ROLE {DatabaseIsolationContract.OwnerRole}");
        Require(!memberships.Any(item =>
                string.Equals(item.Member, DatabaseIsolationContract.RuntimeRole, StringComparison.Ordinal)),
            $"{DatabaseIsolationContract.RuntimeRole} must not be a member of any role");
        Require(!memberships.Any(item =>
                string.Equals(item.Role, DatabaseIsolationContract.RuntimeRole, StringComparison.Ordinal)),
            "no role may be a member of the runtime role");
        Require(!memberships.Any(item =>
                string.Equals(item.Member, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal) &&
                !string.Equals(item.Role, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal)),
            $"{DatabaseIsolationContract.OwnerRole} must not inherit a privileged role");

        if (requireRuntimeLogin)
        {
            IdentityRow identity = await context.Database.SqlQueryRaw<IdentityRow>("""
                SELECT session_user AS "SessionUser",
                       current_user AS "CurrentUser",
                       (SELECT rolsuper FROM pg_roles WHERE rolname = session_user) AS "SessionIsSuperuser",
                       (SELECT rolbypassrls FROM pg_roles WHERE rolname = session_user) AS "SessionBypassRls",
                       (SELECT rolcanlogin FROM pg_roles WHERE rolname = session_user) AS "SessionCanLogin"
                """).SingleAsync(cancellationToken);
            Require(string.Equals(identity.SessionUser, DatabaseIsolationContract.RuntimeRole, StringComparison.Ordinal) &&
                    string.Equals(identity.CurrentUser, DatabaseIsolationContract.RuntimeRole, StringComparison.Ordinal),
                $"normal startup must authenticate as {DatabaseIsolationContract.RuntimeRole}, not session_user={identity.SessionUser}/current_user={identity.CurrentUser}");
            Require(identity.SessionCanLogin && !identity.SessionIsSuperuser && !identity.SessionBypassRls,
                $"normal startup login {identity.SessionUser} must be a non-superuser login without RLS bypass");
        }

        RelationRow[] relations = await ReadRelationsAsync(context, cancellationToken);
        foreach (string table in DatabaseIsolationContract.ProtectedTables)
        {
            RelationRow relation = relations.SingleOrDefault(item => string.Equals(item.Relation, table, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"isolation drift: protected table '{table}' is missing");
            Require(relation.Kind == "r", $"isolation drift: protected relation '{table}' is not a table");
            Require(relation.RowSecurity, $"isolation drift: RLS is not enabled on '{table}'");
            Require(relation.ForceRowSecurity, $"isolation drift: RLS is not forced on '{table}'");
            Require(string.Equals(relation.Owner, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal),
                $"isolation drift: '{table}' is not owned by {DatabaseIsolationContract.OwnerRole}");
        }

        foreach (string table in DatabaseIsolationContract.ControlPlaneTables.Concat(DatabaseIsolationContract.LedgerTables))
        {
            RelationRow relation = relations.SingleOrDefault(item => string.Equals(item.Relation, table, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"isolation drift: required table '{table}' is missing");
            Require(relation.Kind == "r", $"isolation drift: required relation '{table}' is not a table");
            Require(!relation.RowSecurity, $"isolation drift: control-plane/ledger table '{table}' must not have RLS");
            Require(string.Equals(relation.Owner, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal),
                $"isolation drift: '{table}' is not owned by {DatabaseIsolationContract.OwnerRole}");
        }

        foreach (RelationRow relation in relations)
        {
            if (!string.Equals(relation.Kind, "r", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"isolation drift: unexpected {relation.Kind} relation '{relation.Relation}'");
            }
        }

        PolicyRow[] policies = await ReadPoliciesAsync(context, cancellationToken);
        foreach (string table in DatabaseIsolationContract.ProtectedTables)
        {
            PolicyRow[] tablePolicies = policies.Where(item => string.Equals(item.Table, table, StringComparison.Ordinal)).ToArray();
            Require(tablePolicies.Length == 2, $"isolation drift: '{table}' must have exactly two policies");
            Require(tablePolicies.Any(item =>
                    string.Equals(item.Policy, DatabaseIsolationContract.RuntimePolicy, StringComparison.Ordinal) &&
                    string.Equals(item.Roles, DatabaseIsolationContract.RuntimeRole, StringComparison.Ordinal) &&
                    string.Equals(item.Command, "ALL", StringComparison.Ordinal) &&
                    string.Equals(item.Permissive, "PERMISSIVE", StringComparison.Ordinal) &&
                    PolicyMatches(item.Using, RuntimePolicyUsing, schema) &&
                    PolicyMatches(item.WithCheck, RuntimePolicyWithCheck, schema)),
                $"isolation drift: runtime policy on '{table}' is missing or does not enforce customer equality");
            Require(tablePolicies.Any(item =>
                    string.Equals(item.Policy, DatabaseIsolationContract.OwnerPolicy, StringComparison.Ordinal) &&
                    string.Equals(item.Roles, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal) &&
                    string.Equals(item.Command, "ALL", StringComparison.Ordinal) &&
                    string.Equals(item.Permissive, "PERMISSIVE", StringComparison.Ordinal) &&
                    PolicyMatches(item.Using, "true", schema) &&
                    PolicyMatches(item.WithCheck, "true", schema)),
                $"isolation drift: owner policy on '{table}' is missing or malformed");
        }
        foreach (PolicyRow policy in policies)
        {
            Require(DatabaseIsolationContract.ProtectedTables.Contains(policy.Table, StringComparer.Ordinal),
                $"isolation drift: unexpected policy '{policy.Policy}' on '{policy.Table}'");
        }

        FunctionRow[] functions = await ReadFunctionsAsync(context, cancellationToken);
        ValidateFunction(functions, DatabaseIsolationContract.MerchantKeyFunction, 1, securityDefiner: false, "i", "s", schema);
        ValidateFunction(functions, DatabaseIsolationContract.AuthorityFunction, 0, securityDefiner: false, "v", "u", schema,
            expectedReturnType: "uuid", expectedBody: CustomerIsolationSql.AuthorityFunctionBody, requireFixedSearchPath: true);
        ValidateFunction(functions, DatabaseIsolationContract.ImportCleanupFunction, 0, securityDefiner: true, "v", "u", schema,
            expectedReturnType: "jsonb", expectedBody: CustomerIsolationSql.ImportCleanupBody, requireFixedSearchPath: true);
        ValidateFunction(functions, DatabaseIsolationContract.TransactionCleanupFunction, 0, securityDefiner: true, "v", "u", schema,
            expectedReturnType: "jsonb", expectedBody: CustomerIsolationSql.TransactionCleanupBody, requireFixedSearchPath: true);
        foreach (FunctionRow function in functions)
        {
            if (function.Name is DatabaseIsolationContract.MerchantKeyFunction or
                DatabaseIsolationContract.AuthorityFunction or
                DatabaseIsolationContract.ImportCleanupFunction or
                DatabaseIsolationContract.TransactionCleanupFunction)
            {
                continue;
            }

            throw new InvalidOperationException($"isolation drift: unexpected function '{function.Name}'");
        }

        TablePrivilegeRow[] tablePrivileges = await ReadTablePrivilegesAsync(context, cancellationToken);
        foreach (string table in DatabaseIsolationContract.ControlPlaneTables.Concat(DatabaseIsolationContract.LedgerTables))
        {
            TablePrivilegeRow privilege = tablePrivileges.Single(item => string.Equals(item.Table, table, StringComparison.Ordinal));
            Require(!privilege.Truncate && !privilege.References && !privilege.Trigger,
                $"isolation drift: runtime has a broad grant on control-plane/ledger table '{table}'");
        }

        foreach (string table in DatabaseIsolationContract.ProtectedTables)
        {
            TablePrivilegeRow privilege = tablePrivileges.Single(item => string.Equals(item.Table, table, StringComparison.Ordinal));
            Require(privilege.Select, $"isolation drift: runtime lacks SELECT on '{table}'");
            Require(privilege.Insert, $"isolation drift: runtime lacks INSERT on '{table}'");
            Require(privilege.Delete == DatabaseIsolationContract.RuntimeDeleteTables.Contains(table, StringComparer.Ordinal),
                $"isolation drift: runtime DELETE grant on '{table}' differs from the contract");
            Require(!privilege.Truncate && !privilege.References && !privilege.Trigger,
                $"isolation drift: runtime has a broad grant on '{table}'");
        }

        ColumnPrivilegeRow[] columnPrivileges = await ReadColumnPrivilegesAsync(context, cancellationToken);
        foreach (IGrouping<string, ColumnPrivilegeRow> group in columnPrivileges
                     .Where(item => !DatabaseIsolationContract.ControlPlaneTables.Contains(item.Table, StringComparer.Ordinal))
                     .GroupBy(item => item.Table, StringComparer.Ordinal))
        {
            string[] actualUpdates = group
                .Where(item => item.CanUpdate)
                .Select(item => item.Column)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] expected = DatabaseIsolationContract.RuntimeUpdateColumns.TryGetValue(group.Key, out string[]? columns)
                ? columns.OrderBy(name => name, StringComparer.Ordinal).ToArray()
                : [];
            Require(actualUpdates.SequenceEqual(expected, StringComparer.Ordinal),
                $"isolation drift: runtime UPDATE grants on '{group.Key}' are {string.Join(",", actualUpdates)}; expected {string.Join(",", expected)}");
        }

        ColumnPrivilegeRow[] customerColumns = columnPrivileges
            .Where(item => string.Equals(item.Table, "customers", StringComparison.Ordinal))
            .ToArray();
        Require(customerColumns.Where(item => item.CanInsert).Select(item => item.Column)
                .OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(DatabaseIsolationContract.CustomerInsertColumns.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal),
            "isolation drift: runtime customer INSERT columns differ from the contract");
        Require(customerColumns.Where(item => item.CanUpdate).Select(item => item.Column)
                .OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(DatabaseIsolationContract.CustomerUpdateColumns.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal),
            "isolation drift: runtime customer UPDATE columns differ from the contract");

        string[] sessionUpdateColumns = columnPrivileges
            .Where(item => string.Equals(item.Table, "sessions", StringComparison.Ordinal) && item.CanUpdate)
            .Select(item => item.Column)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Require(sessionUpdateColumns.SequenceEqual(["last_seen_at", "revoked_at"], StringComparer.Ordinal),
            "isolation drift: runtime session UPDATE columns differ from the contract");

        foreach (string tokenTable in new[] { "email_verification_tokens", "password_reset_tokens" })
        {
            string[] tokenUpdateColumns = columnPrivileges
                .Where(item => string.Equals(item.Table, tokenTable, StringComparison.Ordinal) && item.CanUpdate)
                .Select(item => item.Column)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Require(tokenUpdateColumns.SequenceEqual(["consumed_at"], StringComparer.Ordinal),
                $"isolation drift: runtime {tokenTable} UPDATE columns differ from the contract");
        }

        foreach (string ledger in DatabaseIsolationContract.LedgerTables)
        {
            TablePrivilegeRow privilege = tablePrivileges.Single(item => string.Equals(item.Table, ledger, StringComparison.Ordinal));
            Require(privilege.Select, $"isolation drift: runtime lacks SELECT on ledger '{ledger}'");
            Require(!privilege.Insert && !privilege.Delete, $"isolation drift: runtime can mutate ledger '{ledger}'");
        }

        TablePrivilegeRow customerPrivilege = tablePrivileges.Single(item => string.Equals(item.Table, "customers", StringComparison.Ordinal));
        Require(customerPrivilege.Select, "isolation drift: runtime lacks SELECT on customers");
        Require(!customerPrivilege.Delete, "isolation drift: runtime can delete customers");

        foreach (string tokenTable in new[] { "sessions", "email_verification_tokens", "password_reset_tokens" })
        {
            TablePrivilegeRow privilege = tablePrivileges.Single(item => string.Equals(item.Table, tokenTable, StringComparison.Ordinal));
            Require(privilege.Select && privilege.Insert, $"isolation drift: runtime lacks SELECT/INSERT on '{tokenTable}'");
            Require(!privilege.Delete, $"isolation drift: runtime can delete '{tokenTable}'");
        }

        Require(!await HasSchemaCreateAsync(context, cancellationToken),
            $"isolation drift: runtime must not have CREATE on schema {schema}");
        Require(await HasSchemaUsageAsync(context, cancellationToken),
            $"isolation drift: runtime must have USAGE on schema {schema}");
    }

    private static async Task<string> RequireCurrentSchemaAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        string? schema = await context.Database
            .SqlQueryRaw<string?>("SELECT current_schema() AS \"Value\"")
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(schema))
        {
            throw new InvalidOperationException("isolation validation requires a resolvable current schema");
        }

        return schema;
    }

    private static async Task<RoleRow[]> ReadRolesAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        return await context.Database.SqlQueryRaw<RoleRow>("""
            SELECT rolname AS "RoleName",
                   rolsuper AS "IsSuperuser",
                   rolbypassrls AS "BypassRls",
                   rolcanlogin AS "CanLogin",
                   rolinherit AS "Inherit",
                   rolcreatedb AS "CreateDb",
                   rolcreaterole AS "CreateRole"
            FROM pg_roles
            WHERE rolname IN ({0}, {1}, {2})
            """,
            DatabaseIsolationContract.OwnerRole,
            DatabaseIsolationContract.MigratorRole,
            DatabaseIsolationContract.RuntimeRole).ToArrayAsync(cancellationToken);
    }

    private static RoleRow RequireRole(RoleRow[] roles, string name) =>
        roles.SingleOrDefault(item => string.Equals(item.RoleName, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"isolation drift: required role '{name}' is missing");

    private static async Task<MembershipRow[]> ReadMembershipsAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        return await context.Database.SqlQueryRaw<MembershipRow>("""
            SELECT member.rolname AS "Member",
                   granted.rolname AS "Role",
                   am.admin_option AS "AdminOption",
                   am.set_option AS "SetOption",
                   am.inherit_option AS "InheritOption"
            FROM pg_auth_members am
            JOIN pg_roles member ON member.oid = am.member
            JOIN pg_roles granted ON granted.oid = am.roleid
            WHERE member.rolname IN ({0}, {1}, {2})
               OR granted.rolname IN ({0}, {1}, {2})
            """,
            DatabaseIsolationContract.OwnerRole,
            DatabaseIsolationContract.MigratorRole,
            DatabaseIsolationContract.RuntimeRole).ToArrayAsync(cancellationToken);
    }

    private static async Task<RelationRow[]> ReadRelationsAsync(ArgusDbContext context, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<RelationRow>("""
            SELECT c.relname AS "Relation",
                   c.relkind::text AS "Kind",
                   c.relrowsecurity AS "RowSecurity",
                   c.relforcerowsecurity AS "ForceRowSecurity",
                   pg_get_userbyid(c.relowner) AS "Owner"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND c.relkind IN ('r', 'v', 'm', 'p', 'f')
            ORDER BY c.relname
            """).ToArrayAsync(cancellationToken);

    private static async Task<PolicyRow[]> ReadPoliciesAsync(ArgusDbContext context, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<PolicyRow>("""
            SELECT schemaname AS "Schema",
                   tablename AS "Table",
                   policyname AS "Policy",
                   permissive AS "Permissive",
                   array_to_string(roles, ',') AS "Roles",
                   cmd AS "Command",
                   qual AS "Using",
                   with_check AS "WithCheck"
            FROM pg_policies
            WHERE schemaname = current_schema()
            ORDER BY tablename, policyname
            """).ToArrayAsync(cancellationToken);

    private static async Task<FunctionRow[]> ReadFunctionsAsync(ArgusDbContext context, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<FunctionRow>("""
            SELECT p.proname AS "Name",
                   p.pronargs::int AS "ArgumentCount",
                   pg_catalog.format_type(p.prorettype, NULL) AS "ReturnType",
                   p.prosecdef AS "SecurityDefiner",
                   p.provolatile::text AS "Volatility",
                   p.proparallel::text AS "Parallel",
                   pg_get_userbyid(p.proowner) AS "Owner",
                   p.proconfig AS "Config",
                   COALESCE(array_to_string(p.proconfig, ','), '') AS "ConfigText",
                   p.prosrc AS "Body",
                   p.proacl IS NOT NULL AS "HasAcl",
                   COALESCE((
                       SELECT string_agg(
                           CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE pg_get_userbyid(acl.grantee) END
                           || ':' || acl.privilege_type
                           || CASE WHEN acl.is_grantable THEN '*' ELSE '' END, ',' ORDER BY 1)
                       FROM aclexplode(p.proacl) acl
                   ), '') AS "AclText",
                   EXISTS (
                       SELECT 1
                       FROM aclexplode(p.proacl) acl
                       WHERE acl.grantee = 0 AND acl.privilege_type = 'EXECUTE'
                   ) AS "PublicExecute",
                   has_function_privilege({0}, p.oid, 'EXECUTE') AS "RuntimeExecute"
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = current_schema()
            ORDER BY p.proname
            """, DatabaseIsolationContract.RuntimeRole).ToArrayAsync(cancellationToken);

    private static void ValidateFunction(FunctionRow[] functions,
                                         string name,
                                         int argumentCount,
                                         bool securityDefiner,
                                         string volatility,
                                         string parallel,
                                         string schema,
                                         string? expectedReturnType = null,
                                         string? expectedBody = null,
                                         bool requireFixedSearchPath = false)
    {
        FunctionRow? function = functions.SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (function is null)
        {
            throw new InvalidOperationException($"isolation drift: function '{name}' is missing");
        }

        Require(function.ArgumentCount == argumentCount, $"isolation drift: function '{name}' has the wrong argument count");
        if (expectedReturnType is not null)
        {
            Require(string.Equals(function.ReturnType, expectedReturnType, StringComparison.Ordinal),
                $"isolation drift: function '{name}' return type changed");
        }
        Require(function.SecurityDefiner == securityDefiner, $"isolation drift: function '{name}' security mode changed");
        Require(string.Equals(function.Volatility, volatility, StringComparison.Ordinal),
            $"isolation drift: function '{name}' volatility changed");
        Require(string.Equals(function.Parallel, parallel, StringComparison.Ordinal),
            $"isolation drift: function '{name}' parallel mode changed");
        Require(string.Equals(function.Owner, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal),
            $"isolation drift: function '{name}' is not owned by {DatabaseIsolationContract.OwnerRole}");
        Require(function.HasAcl, $"isolation drift: function '{name}' still has default PUBLIC EXECUTE");
        Require(!function.PublicExecute, $"isolation drift: function '{name}' is executable by PUBLIC");
        Require(function.RuntimeExecute, $"isolation drift: runtime cannot execute '{name}'");
        string[] aclEntries = function.AclText.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Require(aclEntries.All(item =>
                string.Equals(item, $"{DatabaseIsolationContract.OwnerRole}:EXECUTE", StringComparison.Ordinal) ||
                string.Equals(item, $"{DatabaseIsolationContract.RuntimeRole}:EXECUTE", StringComparison.Ordinal)),
            $"isolation drift: function '{name}' grants EXECUTE beyond owner and runtime");
        Require(aclEntries.Contains($"{DatabaseIsolationContract.OwnerRole}:EXECUTE", StringComparer.Ordinal) &&
                aclEntries.Contains($"{DatabaseIsolationContract.RuntimeRole}:EXECUTE", StringComparer.Ordinal),
            $"isolation drift: function '{name}' is missing an explicit owner/runtime EXECUTE grant");

        if (securityDefiner)
        {
            Require(function.Config is not null && function.Config.Any(item => item.StartsWith("search_path=", StringComparison.Ordinal)),
                $"isolation drift: definer function '{name}' lacks a fixed search_path");
        }
        if (requireFixedSearchPath)
        {
            Require(string.Equals(function.ConfigText, $"search_path={schema}, pg_catalog, pg_temp", StringComparison.Ordinal),
                $"isolation drift: function '{name}' search_path is not pinned to {schema}, pg_catalog, pg_temp");
        }
        if (expectedBody is not null)
        {
            Require(string.Equals(NormalizeBody(function.Body), NormalizeBody(expectedBody), StringComparison.Ordinal),
                $"isolation drift: function '{name}' body differs from the expected definition");
        }
    }

    private static bool PolicyMatches(string actual, string expected, string schema)
    {
        string normalized = NormalizeExpression(actual).Replace($"{NormalizeExpression(schema)}.", string.Empty, StringComparison.Ordinal);
        return string.Equals(normalized, NormalizeExpression(expected), StringComparison.Ordinal);
    }

    private static string NormalizeExpression(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal);

    private static string NormalizeBody(string value) =>
        string.Join('\n', value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.Trim()))
            .Trim();

    private static async Task<TablePrivilegeRow[]> ReadTablePrivilegesAsync(ArgusDbContext context, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<TablePrivilegeRow>("""
            SELECT c.relname AS "Table",
                   has_table_privilege({0}, c.oid, 'SELECT') AS "Select",
                   has_table_privilege({0}, c.oid, 'INSERT') AS "Insert",
                   has_table_privilege({0}, c.oid, 'DELETE') AS "Delete",
                   has_table_privilege({0}, c.oid, 'TRUNCATE') AS "Truncate",
                   has_table_privilege({0}, c.oid, 'REFERENCES') AS "References",
                   has_table_privilege({0}, c.oid, 'TRIGGER') AS "Trigger"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND c.relkind = 'r'
            ORDER BY c.relname
            """, DatabaseIsolationContract.RuntimeRole).ToArrayAsync(cancellationToken);

    private static async Task<ColumnPrivilegeRow[]> ReadColumnPrivilegesAsync(ArgusDbContext context, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<ColumnPrivilegeRow>("""
            SELECT c.relname AS "Table",
                   a.attname AS "Column",
                   has_column_privilege({0}, c.oid, a.attnum, 'INSERT') AS "CanInsert",
                   has_column_privilege({0}, c.oid, a.attnum, 'UPDATE') AS "CanUpdate"
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND c.relkind = 'r'
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY c.relname, a.attname
            """, DatabaseIsolationContract.RuntimeRole).ToArrayAsync(cancellationToken);

    private static async Task<bool> HasSchemaCreateAsync(ArgusDbContext context, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQueryRaw<bool>("SELECT has_schema_privilege({0}, current_schema(), 'CREATE') AS \"Value\"", DatabaseIsolationContract.RuntimeRole)
            .SingleAsync(cancellationToken);

    private static async Task<bool> HasSchemaUsageAsync(ArgusDbContext context, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQueryRaw<bool>("SELECT has_schema_privilege({0}, current_schema(), 'USAGE') AS \"Value\"", DatabaseIsolationContract.RuntimeRole)
            .SingleAsync(cancellationToken);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RoleRow
    {
        public string RoleName { get; set; } = string.Empty;
        public bool IsSuperuser { get; set; }
        public bool BypassRls { get; set; }
        public bool CanLogin { get; set; }
        public bool Inherit { get; set; }
        public bool CreateDb { get; set; }
        public bool CreateRole { get; set; }
    }

    private sealed class MembershipRow
    {
        public string Member { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool AdminOption { get; set; }
        public bool SetOption { get; set; }
        public bool InheritOption { get; set; }
    }

    private sealed class IdentityRow
    {
        public string SessionUser { get; set; } = string.Empty;
        public string CurrentUser { get; set; } = string.Empty;
        public bool SessionIsSuperuser { get; set; }
        public bool SessionBypassRls { get; set; }
        public bool SessionCanLogin { get; set; }
    }

    private sealed class RelationRow
    {
        public string Relation { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public bool RowSecurity { get; set; }
        public bool ForceRowSecurity { get; set; }
        public string Owner { get; set; } = string.Empty;
    }

    private sealed class PolicyRow
    {
        public string Schema { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
        public string Permissive { get; set; } = string.Empty;
        public string Roles { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Using { get; set; } = string.Empty;
        public string WithCheck { get; set; } = string.Empty;
    }

    private sealed class FunctionRow
    {
        public string Name { get; set; } = string.Empty;
        public int ArgumentCount { get; set; }
        public string ReturnType { get; set; } = string.Empty;
        public bool SecurityDefiner { get; set; }
        public string Volatility { get; set; } = string.Empty;
        public string Parallel { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string[]? Config { get; set; }
        public string ConfigText { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string AclText { get; set; } = string.Empty;
        public bool HasAcl { get; set; }
        public bool PublicExecute { get; set; }
        public bool RuntimeExecute { get; set; }
    }

    private sealed class TablePrivilegeRow
    {
        public string Table { get; set; } = string.Empty;
        public bool Select { get; set; }
        public bool Insert { get; set; }
        public bool Delete { get; set; }
        public bool Truncate { get; set; }
        public bool References { get; set; }
        public bool Trigger { get; set; }
    }

    private sealed class ColumnPrivilegeRow
    {
        public string Table { get; set; } = string.Empty;
        public string Column { get; set; } = string.Empty;
        public bool CanInsert { get; set; }
        public bool CanUpdate { get; set; }
    }
}
