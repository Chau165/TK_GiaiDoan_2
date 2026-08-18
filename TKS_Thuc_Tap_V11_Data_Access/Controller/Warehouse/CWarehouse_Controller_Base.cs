using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public abstract class CWarehouse_Controller_Base
{
    protected static SqlConnection CreateConnection() => new(CConfig.TKS_Thuc_Tap_V11_Conn_String);

    protected static async Task ExecuteAsync(string p_strProcedure, Action<SqlCommand> p_objAdd)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(p_strProcedure, connection) { CommandType = CommandType.StoredProcedure };
        p_objAdd(command);
        await command.ExecuteNonQueryAsync();
    }

    protected static async Task<List<T>> ReadAsync<T>(string p_strSql, Func<SqlDataReader, T> p_objMap, Action<SqlCommand>? p_objAdd = null)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(p_strSql, connection);
        p_objAdd?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync();

        var result = new List<T>();
        while (await reader.ReadAsync())
            result.Add(p_objMap(reader));

        return result;
    }

    protected static async Task<List<T>> ReadProcedureAsync<T>(string p_strProcedure, Func<SqlDataReader, T> p_objMap, Action<SqlCommand> p_objAdd)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(p_strProcedure, connection) { CommandType = CommandType.StoredProcedure };
        p_objAdd(command);
        await using var reader = await command.ExecuteReaderAsync();

        var result = new List<T>();
        while (await reader.ReadAsync())
            result.Add(p_objMap(reader));

        return result;
    }

    protected static void Add(SqlCommand p_objCommand, string p_strName, object? p_objValue) =>
        p_objCommand.Parameters.AddWithValue(p_strName, p_objValue ?? DBNull.Value);
}
