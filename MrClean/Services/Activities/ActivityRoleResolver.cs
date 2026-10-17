using System.Collections.Concurrent;

namespace MrClean.Services.Activities;

public record ActivityRoleCandidate(ulong Id, string Name, bool Safe);
public interface IActivityRoleCatalog
{
    Task<IReadOnlyList<ActivityRoleCandidate>> ListAsync(ulong guild);
    Task<ActivityRoleCandidate> CreateAsync(ulong guild, string name);
}
public class ActivityRoleResolver
{
    private readonly IActivityRoleCatalog _catalog;
    private readonly ConcurrentDictionary<(ulong Guild, string Name), byte> _uncertain = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new();
    public ActivityRoleResolver(IActivityRoleCatalog catalog) => _catalog = catalog;
    public async Task<ulong> ResolveAsync(ulong guild, ulong? selected, string? createName, ulong connected)
    {
        if (guild == 0 || connected == 0) throw new ArgumentException("Configure the guild and protected Connected role first.");
        if ((selected == null) == (createName == null)) throw new ArgumentException("Select a role OR request create-role with a name.");
        if (createName != null && (string.IsNullOrWhiteSpace(createName) || createName.Length > 100 || createName.Any(char.IsControl)))
            throw new ArgumentException("Role names must be nonempty single-line values up to 100 characters.");
        var gate = _gates.GetOrAdd(guild, _ => new SemaphoreSlim(1)); await gate.WaitAsync();
        try
        {
            var roles = await _catalog.ListAsync(guild);
            var matches = roles.Where(x => selected == null ? string.Equals(x.Name, createName, StringComparison.Ordinal) : x.Id == selected).ToArray();
            if (matches.Length > 1) throw new ArgumentException("Role name is ambiguous; select the exact existing role.");
            if (matches.Length == 1)
            {
                var role = matches[0];
                if (!role.Safe || role.Id == connected || role.Id == guild || role.Id == 0) throw new ArgumentException("Choose a manageable ordinary role below Mr-Clean; Connected is protected.");
                if (createName != null) _uncertain.TryRemove((guild, createName), out _);
                return role.Id;
            }
            if (selected != null) throw new ArgumentException("Selected role no longer exists.");
            if (_uncertain.ContainsKey((guild, createName!))) throw new ArgumentException("Previous role creation had an uncertain outcome. Inspect server roles and select an existing role ID before retrying creation.");
            ActivityRoleCandidate created;
            try { created = await _catalog.CreateAsync(guild, createName!); }
            catch (ArgumentException) { throw; } // Local validation/permission rejection: no request succeeded.
            catch (Discord.Net.HttpException ex) when (ex.HttpCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.NotFound)
            { throw; } // Definite Discord rejection is safe to retry after correction.
            catch
            {
                _uncertain[(guild, createName!)] = 0;
                throw;
            }
            if (!created.Safe) throw new InvalidOperationException("Created role cannot be managed; configure hierarchy and select it explicitly. No role was deleted.");
            return created.Id;
        }
        finally { gate.Release(); }
    }
}
