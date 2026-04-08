using Microsoft.Data.Sqlite;

namespace LootMaster.Services;

/// <summary>
/// Synchronises loot assignments back into the ArcheAge SQLite database.
///
/// Schema relations:
///   loot_pack_dropping_npcs : npc_id → loot_pack_id
///   loot_groups             : pack_id + group_no → drop_rate  (group-level chance)
///   loots                   : loot_pack_id + item_id + group  → drop_rate (item-level chance)
///
/// Strategy:
///   • Only rows reachable via the loaded JSON (npc_id → item_id pairs) are touched.
///   • loot_groups.drop_rate is set to 10 000 000 (100 %) for every affected group
///     so that the effective item chance equals loots.drop_rate / 10 000 000.
///   • loots.group and loots.drop_rate are updated for every matching row.
///   • Everything runs in a single transaction — all or nothing.
/// </summary>
public sealed class DbSyncService(string dbPath)
{
    private const int MaxDropRate = 10_000_000;

    public record SyncPreview(int LootRows, int LootGroupRows);

    /// <summary>
    /// Calculates how many rows will be affected without writing anything.
    /// </summary>
    public async Task<SyncPreview> PreviewAsync(
        IReadOnlyList<SyncItem> items,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var conn = OpenConnection(readOnly: true);
            var (lootRows, groupRows) = CountAffected(conn, items);
            return new SyncPreview(lootRows, groupRows);
        }, ct);
    }

    /// <summary>
    /// Writes all assignments to the database inside a single transaction.
    /// </summary>
    public async Task SyncAsync(
        IReadOnlyList<SyncItem> items,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var conn = OpenConnection(readOnly: false);
            using var tx = conn.BeginTransaction();

            int done = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                int dropRate = (int)Math.Round(item.Chance * MaxDropRate / 100.0);

                // 1. Update loots rows for all packs that contain this NPC+item pair
                foreach (int packId in GetPackIds(conn, item.NpcIds))
                {
                    // Update loots
                    using var cmdLoot = conn.CreateCommand();
                    cmdLoot.Transaction = tx;
                    cmdLoot.CommandText = """
                        UPDATE loots
                           SET "group"    = @grp,
                               drop_rate  = @rate
                         WHERE loot_pack_id = @pack
                           AND item_id      = @item
                        """;
                    cmdLoot.Parameters.AddWithValue("@grp",  item.Group);
                    cmdLoot.Parameters.AddWithValue("@rate", dropRate);
                    cmdLoot.Parameters.AddWithValue("@pack", packId);
                    cmdLoot.Parameters.AddWithValue("@item", item.ItemId);
                    cmdLoot.ExecuteNonQuery();

                    // 2. Ensure loot_groups row exists for this pack+group, set drop_rate = 100%
                    using var cmdGroup = conn.CreateCommand();
                    cmdGroup.Transaction = tx;
                    cmdGroup.CommandText = """
                        UPDATE loot_groups
                           SET drop_rate = @maxRate
                         WHERE pack_id  = @pack
                           AND group_no = @grp
                        """;
                    cmdGroup.Parameters.AddWithValue("@maxRate", MaxDropRate);
                    cmdGroup.Parameters.AddWithValue("@pack",    packId);
                    cmdGroup.Parameters.AddWithValue("@grp",     item.Group);
                    cmdGroup.ExecuteNonQuery();
                }

                done++;
                if (done % 50 == 0)
                    progress?.Report($"Записываю в БД… {done}/{items.Count}");
            }

            tx.Commit();
            progress?.Report($"Готово. Записано предметов: {items.Count}");
        }, ct);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private SqliteConnection OpenConnection(bool readOnly)
    {
        var mode = readOnly ? "ReadOnly" : "ReadWrite";
        var conn = new SqliteConnection($"Data Source={dbPath};Mode={mode};");
        conn.Open();
        return conn;
    }

    private static List<int> GetPackIds(SqliteConnection conn, IReadOnlyList<int> npcIds)
    {
        if (npcIds.Count == 0) return [];

        var placeholders = string.Join(',', Enumerable.Range(0, npcIds.Count).Select(i => $"@n{i}"));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT DISTINCT loot_pack_id FROM loot_pack_dropping_npcs WHERE npc_id IN ({placeholders})";
        for (int i = 0; i < npcIds.Count; i++)
            cmd.Parameters.AddWithValue($"@n{i}", npcIds[i]);

        List<int> result = [];
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetInt32(0));
        return result;
    }

    private static (int lootRows, int groupRows) CountAffected(SqliteConnection conn, IReadOnlyList<SyncItem> items)
    {
        int lootRows = 0, groupRows = 0;
        foreach (var item in items)
        {
            var packIds = GetPackIds(conn, item.NpcIds);
            foreach (int packId in packIds)
            {
                using var cmdL = conn.CreateCommand();
                cmdL.CommandText = "SELECT COUNT(*) FROM loots WHERE loot_pack_id=@p AND item_id=@i";
                cmdL.Parameters.AddWithValue("@p", packId);
                cmdL.Parameters.AddWithValue("@i", item.ItemId);
                lootRows += Convert.ToInt32(cmdL.ExecuteScalar());

                using var cmdG = conn.CreateCommand();
                cmdG.CommandText = "SELECT COUNT(*) FROM loot_groups WHERE pack_id=@p AND group_no=@g";
                cmdG.Parameters.AddWithValue("@p", packId);
                cmdG.Parameters.AddWithValue("@g", item.Group);
                groupRows += Convert.ToInt32(cmdG.ExecuteScalar());
            }
        }
        return (lootRows, groupRows);
    }
}

/// <summary>
/// One item assignment ready to be written to the database.
/// </summary>
public sealed record SyncItem(int ItemId, IReadOnlyList<int> NpcIds, int Group, double Chance);
