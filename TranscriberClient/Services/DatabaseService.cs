using System.Globalization;
using MySqlConnector;
using TranscriberClient.Models;

namespace TranscriberClient.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string? connectionString = null)
    {
        _connectionString = connectionString ?? AppSettings.DatabaseConnectionString;
    }

    public MySqlConnection CreateConnection()
    {
        return new MySqlConnection(_connectionString);
    }

    public async Task<UserAccount?> GetUserByUsernameAsync(string username)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = @"SELECT id, username, full_name, gender, role, prof_pic, id_card, password, req_date, status
                             FROM req_acc
                             WHERE username = @Username AND status = 'Active' AND role = 'Transcriber'";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Username", username);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new UserAccount
        {
            Id = reader.GetInt32("id"),
            Username = reader.GetString("username"),
            FullName = reader.IsDBNull(reader.GetOrdinal("full_name")) ? string.Empty : reader.GetString("full_name"),
            Gender = reader.IsDBNull(reader.GetOrdinal("gender")) ? string.Empty : reader.GetString("gender"),
            Role = reader.IsDBNull(reader.GetOrdinal("role")) ? string.Empty : reader.GetString("role"),
            ProfPic = reader.IsDBNull(reader.GetOrdinal("prof_pic")) ? string.Empty : reader.GetString("prof_pic"),
            IdCard = reader.IsDBNull(reader.GetOrdinal("id_card")) ? string.Empty : reader.GetString("id_card"),
            PasswordHash = reader.IsDBNull(reader.GetOrdinal("password")) ? string.Empty : reader.GetString("password"),
            ReqDate = reader.IsDBNull(reader.GetOrdinal("req_date")) ? null : reader.GetDateTime("req_date"),
            Status = reader.IsDBNull(reader.GetOrdinal("status")) ? string.Empty : reader.GetString("status")
        };
    }

    public async Task<List<Record>> GetRecordsForUserAsync(string username)
    {
        var results = new List<Record>();

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = @"SELECT * FROM records
                             WHERE transcriber = @Username
                               AND status IN ('Assigned', 'Pending', 'Suspended', 'Finished')
                             ORDER BY id DESC";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Username", username);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapRecord(reader));
        }

        return results;
    }

    public async Task AttachLocalAudioAsync(int recordId, string username, string audioFileName)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = @"UPDATE records
                             SET audio = @Audio, audio_status = 'Local audio'
                             WHERE id = @Id AND transcriber = @Username
                               AND status IN ('Assigned', 'Pending', 'Suspended')";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Audio", audioFileName);
        command.Parameters.AddWithValue("@Id", recordId);
        command.Parameters.AddWithValue("@Username", username);
        if (await command.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException("The audio was not attached. The assignment may have changed or may no longer belong to your account.");
        }
    }

    public async Task ChangeTranscriberPasswordAsync(UserAccount user, string currentPassword, string newPassword)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        const string readSql = @"SELECT password FROM req_acc
                                 WHERE id = @Id AND username = @Username
                                   AND status = 'Active' AND role = 'Transcriber'
                                 LIMIT 1";
        await using var readCommand = new MySqlCommand(readSql, connection);
        readCommand.Parameters.AddWithValue("@Id", user.Id);
        readCommand.Parameters.AddWithValue("@Username", user.Username);
        var storedHash = Convert.ToString(await readCommand.ExecuteScalarAsync());

        if (!Helpers.PasswordHasher.Verify(currentPassword, storedHash))
        {
            throw new InvalidOperationException("The current password is incorrect.");
        }

        var newHash = Helpers.PasswordHasher.Hash(newPassword);
        const string updateSql = @"UPDATE req_acc SET password = @Password
                                   WHERE id = @Id AND username = @Username
                                     AND status = 'Active' AND role = 'Transcriber'";
        await using var updateCommand = new MySqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("@Password", newHash);
        updateCommand.Parameters.AddWithValue("@Id", user.Id);
        updateCommand.Parameters.AddWithValue("@Username", user.Username);
        if (await updateCommand.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException("The password was not changed because the account is no longer active.");
        }

        user.PasswordHash = newHash;
    }

    public async Task SaveAudioProgressAsync(string machineNum, double position)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var countCommand = new MySqlCommand("SELECT COUNT(*) FROM audio_progress WHERE machine_num = @MachineNum", connection);
        countCommand.Parameters.AddWithValue("@MachineNum", machineNum);

        var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        var value = position.ToString(CultureInfo.InvariantCulture);

        if (existingCount > 0)
        {
            var update = new MySqlCommand("UPDATE audio_progress SET last_position = @Position WHERE machine_num = @MachineNum", connection);
            update.Parameters.AddWithValue("@Position", value);
            update.Parameters.AddWithValue("@MachineNum", machineNum);
            await update.ExecuteNonQueryAsync();
            return;
        }

        var insert = new MySqlCommand("INSERT INTO audio_progress (machine_num, last_position) VALUES (@MachineNum, @Position)", connection);
        insert.Parameters.AddWithValue("@MachineNum", machineNum);
        insert.Parameters.AddWithValue("@Position", value);
        await insert.ExecuteNonQueryAsync();
    }

    public async Task<double> LoadAudioProgressAsync(string machineNum)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = "SELECT last_position FROM audio_progress WHERE machine_num = @MachineNum LIMIT 1";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@MachineNum", machineNum);

        var result = await command.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
        {
            return 0d;
        }

        return double.TryParse(result.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0d;
    }

    public async Task<bool> UpdateRecordStatusAsync(int id, string newStatus, string remark)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        if (newStatus == "Finished")
        {
            const string sql = "UPDATE records SET status = @Status, remark = @Remark, finished_date = CURDATE() WHERE id = @Id";
            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Status", newStatus);
            command.Parameters.AddWithValue("@Remark", remark ?? string.Empty);
            command.Parameters.AddWithValue("@Id", id);
            await command.ExecuteNonQueryAsync();
            return true;
        }

        const string updateSql = "UPDATE records SET status = @Status, remark = @Remark WHERE id = @Id";
        await using var command2 = new MySqlCommand(updateSql, connection);
        command2.Parameters.AddWithValue("@Status", newStatus);
        command2.Parameters.AddWithValue("@Remark", remark ?? string.Empty);
        command2.Parameters.AddWithValue("@Id", id);
        await command2.ExecuteNonQueryAsync();
        return true;
    }

    private static Record MapRecord(MySqlDataReader reader)
    {
        var record = new Record
        {
            Id = reader.GetInt32("id"),
            FileNum = reader.IsDBNull(reader.GetOrdinal("file_num")) ? 0 : reader.GetInt32("file_num"),
            MachineNum = reader.IsDBNull(reader.GetOrdinal("machine_num")) ? 0 : reader.GetInt32("machine_num"),
            Applicant = reader.IsDBNull(reader.GetOrdinal("Apllicant")) ? string.Empty : reader.GetString("Apllicant"),
            Defendant = reader.IsDBNull(reader.GetOrdinal("Defendent")) ? string.Empty : reader.GetString("Defendent"),
            WitnessType = reader.IsDBNull(reader.GetOrdinal("witness_type")) ? string.Empty : reader.GetString("witness_type"),
            Witnesses = reader.IsDBNull(reader.GetOrdinal("witnesses")) ? string.Empty : reader.GetString("witnesses"),
            Trial = reader.IsDBNull(reader.GetOrdinal("trial")) ? string.Empty : reader.GetString("trial"),
            Judge = reader.IsDBNull(reader.GetOrdinal("judge")) ? string.Empty : reader.GetString("judge"),
            Audio = reader.IsDBNull(reader.GetOrdinal("audio")) ? string.Empty : reader.GetString("audio"),
            AudioStatus = reader.IsDBNull(reader.GetOrdinal("audio_status")) ? string.Empty : reader.GetString("audio_status"),
            Remark = reader.IsDBNull(reader.GetOrdinal("remark")) ? string.Empty : reader.GetString("remark"),
            Recorder = reader.IsDBNull(reader.GetOrdinal("recorder")) ? string.Empty : reader.GetString("recorder"),
            Transcriber = reader.IsDBNull(reader.GetOrdinal("transcriber")) ? string.Empty : reader.GetString("transcriber"),
            Status = reader.IsDBNull(reader.GetOrdinal("status")) ? string.Empty : reader.GetString("status"),
            Doc = reader.IsDBNull(reader.GetOrdinal("doc")) ? string.Empty : reader.GetString("doc"),
            RecDate = ReadNullableDate(reader, "rec_date"),
            AppointedOn = ReadNullableDate(reader, "appointed_on"),
            InsertedOn = ReadNullableDate(reader, "inserted_on"),
            DistributedOn = ReadNullableDate(reader, "distrubuted_on"),
            FinishedDate = ReadNullableDate(reader, "finished_date")
        };

        return record;
    }

    private static DateTime? ReadNullableDate(MySqlDataReader reader, string columnName)
    {
        if (reader.IsDBNull(reader.GetOrdinal(columnName)))
        {
            return null;
        }

        var value = reader.GetDateTime(columnName);
        return value == DateTime.MinValue ? null : value;
    }
}
