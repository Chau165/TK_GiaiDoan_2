using System;
using System.Collections;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Configuration;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.DataLayer
{
    public sealed class CSqlHelper
    {
        //public static int m_iQueue_Sleep = 10;

		#region public function
		public static SqlConnection CreateConnection(string p_strConnStr)
        {
            SqlConnection v_conn = new SqlConnection();
            v_conn.ConnectionString = p_strConnStr;
            return v_conn;
        }

		public static int ExecuteNonquery(string p_strConnStr, string p_strSPname, params object[] p_arrValue)
        {
            //long v_iSTT = CUtility_Queue_DB.Add_Queue(p_strSPname);
            //long v_iCurrent_Queue = CUtility_Queue_DB.Get_Current_Queue(p_strSPname);

            //if (CConfig.Khach_Hang_ID != (int)EKhach_Hang_ID.AD)
            //{
            //    Thread.Sleep(m_iQueue_Sleep);

            //    while (v_iCurrent_Queue != v_iSTT)
            //    {
            //        Thread.Sleep(m_iQueue_Sleep);
            //        v_iCurrent_Queue = CUtility_Queue_DB.Get_Current_Queue(p_strSPname);
            //    }
            //}

            try
            {
                if ((p_arrValue != null) && (p_arrValue.Length > 0))
                {
                    // Tạo danh sách SqlParameter
                    SqlParameter[] v_arrSQLParameter = CSqlHelperParameterCache.GetSpParameterSet(
                        p_strConnStr, p_strSPname);

                    // Gán dữ liệu từ các mãng value vô mảng command parameter
                    AssignParameterValues(v_arrSQLParameter, p_arrValue, p_strSPname);

                    // gọi hàm overload
                    return ExecuteNonQuery(p_strConnStr, p_strSPname, v_arrSQLParameter);
                }

                else
                {
                    return ExecuteNonQuery(p_strConnStr, p_strSPname, null);
                }
            }

            catch (Exception)
            {
                throw;
            }

   //         finally
   //         {
   //             CUtility_Queue_DB.Remove_Queue(p_strSPname, v_iSTT);
			//}
        }

        public static int ExecuteNonquery(SqlConnection p_objConn, SqlTransaction p_objTrans, string p_strConnString,
            string p_strSPname, params object[] p_arrValue)
        {
			try
            {
                if ((p_arrValue != null) && (p_arrValue.Length > 0))
                {
                    // Tạo danh sách SqlParameter
                    SqlParameter[] v_arrSQLParameter = CSqlHelperParameterCache.GetSpParameterSet(
                        p_strConnString, p_strSPname);

                    // Gán dữ liệu từ các mãng value vô mảng command parameter
                    AssignParameterValues(v_arrSQLParameter, p_arrValue, p_strSPname);

                    // gọi hàm overload
                    return ExecuteNonQuery(p_objConn, p_objTrans, p_strSPname, v_arrSQLParameter);
                }

                else
                {
                    return ExecuteNonQuery(p_objConn, p_objTrans, p_strSPname, null);
                }
            }

			catch (Exception)
			{
				throw;
			}
		}

        public static int ExecuteNonquery(string p_strConnStr, string p_strSPname, params SqlParameter[] p_arrSQLParameter)
        {
            return ExecuteNonQuery(p_strConnStr, p_strSPname, p_arrSQLParameter);
        }

        public static object ExecuteScalar(string p_strConnStr, string p_strSPname, params object[] p_arrValue)
        {
			try
            {
                if ((p_arrValue != null) && (p_arrValue.Length > 0))
                {
                    // Tạo danh sách SqlParameter
                    SqlParameter[] v_arrSQLParameter = CSqlHelperParameterCache.GetSpParameterSet(
                        p_strConnStr, p_strSPname);

                    // Gán dữ liệu từ các mãng value vô mảng command parameter
                    AssignParameterValues(v_arrSQLParameter, p_arrValue, p_strSPname);

                    // gọi hàm overload
                    return ExecuteScalar(p_strConnStr, p_strSPname, v_arrSQLParameter);
                }

                else
                {
                    return ExecuteScalar(p_strConnStr, p_strSPname, null);
                }
            }

			catch (Exception)
			{
				throw;
			}
		}

        public static object ExecuteScalar(SqlConnection p_conn, SqlTransaction p_trans, string p_strConnStr, string p_strSPname,
            params object[] p_arrValue)
        {
			try
            {
                if ((p_arrValue != null) && (p_arrValue.Length > 0))
                {
                    // Tạo danh sách SqlParameter
                    SqlParameter[] v_arrSQLParameter = CSqlHelperParameterCache.GetSpParameterSet(
                        p_strConnStr, p_strSPname);

                    // Gán dữ liệu từ các mãng value vô mảng command parameter
                    AssignParameterValues(v_arrSQLParameter, p_arrValue, p_strSPname);

                    // gọi hàm overload
                    return ExecuteScalar(p_conn, p_trans, p_strSPname, v_arrSQLParameter);
                }

                else
                {
                    return ExecuteScalar(p_conn, p_trans, p_strConnStr, p_strSPname, null);
                }
            }

			catch (Exception)
			{
				throw;
			}
		}

        public static void FillDataTable(string p_strConnStr, DataTable p_dtData, string p_strSPname, params object[] p_arrValue)
        {
            if ((p_arrValue != null) && (p_arrValue.Length > 0))
            {
                // Tạo danh sách SqlParameter
                SqlParameter[] v_arrSQLParameter = CSqlHelperParameterCache.GetSpParameterSet(
                    p_strConnStr, p_strSPname);

                // Gán dữ liệu từ các mãng value vô mảng command parameter
                AssignParameterValues(v_arrSQLParameter, p_arrValue, p_strSPname);

                // gọi hàm overload
                FillDataTable(p_strConnStr, p_dtData, p_strSPname, v_arrSQLParameter);
            }

            else
            {
                FillDataTable(p_strConnStr, p_dtData, p_strSPname, null);
            }
        }

        public static void FillDataTable(SqlConnection p_conn, SqlTransaction p_trans, string p_strConnStr, DataTable p_dtData,
            string p_strSPname, params object[] p_arrValue)
        {
            if ((p_arrValue != null) && (p_arrValue.Length > 0))
            {
                // Tạo danh sách SqlParameter
                SqlParameter[] v_arrSQLParameter = CSqlHelperParameterCache.GetSpParameterSet(
                    p_strConnStr, p_strSPname);

                // Gán dữ liệu từ các mãng value vô mảng command parameter
                AssignParameterValues(v_arrSQLParameter, p_arrValue, p_strSPname);

                // gọi hàm overload
                FillDataTable(p_conn, p_trans, p_dtData, p_strSPname, v_arrSQLParameter);
            }

            else
            {
                FillDataTable(p_conn, p_trans, p_strConnStr, p_dtData, p_strSPname, null);
            }
        }

        public static void FillDataSet(string p_strConnStr, DataSet p_dsData, string p_strSPname, params object[] p_arrValue)
        {
            SqlParameter[] v_arrSQLParameter = null;
            if ((p_arrValue != null) && (p_arrValue.Length > 0))
            {
                v_arrSQLParameter = CSqlHelperParameterCache.GetSpParameterSet(p_strConnStr, p_strSPname);
                AssignParameterValues(v_arrSQLParameter, p_arrValue, p_strSPname);
            }

            FillDataSet(p_strConnStr, p_dsData, p_strSPname, v_arrSQLParameter);
        }

		public static void FillDataTable_Cmd(string p_strConnStr, DataTable p_dtData, string p_strCmd)
		{
			SqlConnection v_conn = new SqlConnection(p_strConnStr);
			SqlCommand v_cmd = new SqlCommand();
			SqlDataAdapter v_da = new SqlDataAdapter(v_cmd);

			try
			{
				//associate the connection with the command
				v_cmd.Connection = v_conn;

				//set the command text (stored procedure name or SQL statement)
				v_cmd.CommandText = p_strCmd;

				//set the command type
				v_cmd.CommandType = CommandType.Text;
				v_cmd.CommandTimeout = 300;
				v_da.Fill(p_dtData);
			}

			catch (Exception)
			{
				throw;
			}

			finally
			{
				if (v_conn.State == ConnectionState.Open)
					v_conn.Close();
				v_cmd.Dispose();
				v_da.Dispose();
			}
		}

		public static void ExecuteNonquery_Cmd(string p_strConnStr, string p_strCmd)
		{
			SqlConnection v_conn = new SqlConnection(p_strConnStr);
			SqlCommand v_cmd = new SqlCommand();

			try
			{
				v_conn.Open();

				//associate the connection with the command
				v_cmd.Connection = v_conn;

				//set the command text (stored procedure name or SQL statement)
				v_cmd.CommandText = p_strCmd;

				//set the command type
				v_cmd.CommandType = CommandType.Text;
				v_cmd.CommandTimeout = 300;

				v_cmd.ExecuteNonQuery();
			}

			catch (Exception)
			{
				throw;
			}

			finally
			{
				if (v_conn.State == ConnectionState.Open)
					v_conn.Close();
				v_cmd.Dispose();
			}
		}

		#endregion

		#region private function
		private static void AssignParameterValues(SqlParameter[] p_arrSQLParameter, object[] p_arrValue, string p_strSPName)
        {
            if ((p_arrSQLParameter == null) || (p_arrValue == null))
            {
                return;
            }

            if (p_arrSQLParameter.Length != p_arrValue.Length)
            {
                throw new Exception($"{p_strSPName}. Parameter count does not match Parameter Value count.");
            }

            for (int v_iIndex = 0, v_iCount = p_arrSQLParameter.Length; v_iIndex < v_iCount; v_iIndex++)
            {
                if (p_arrValue[v_iIndex] == null)
                    p_arrSQLParameter[v_iIndex].Value = DBNull.Value;
                else
                    p_arrSQLParameter[v_iIndex].Value = p_arrValue[v_iIndex];
            }
        }

        private static void AttachParameters(SqlCommand p_cmd, SqlParameter[] p_arrSQLParameter)
        {
            foreach (SqlParameter v_Parameter in p_arrSQLParameter)
            {
                if ((v_Parameter.Direction == ParameterDirection.InputOutput) && (v_Parameter.Value == null))
                {
                    v_Parameter.Value = DBNull.Value;
                }

                p_cmd.Parameters.Add(v_Parameter);
            }
        }

        private static int ExecuteNonQuery(string p_strConnStr, string p_strStoreName,
            params SqlParameter[] p_arrSQLParameter)
        {
            DateTime? v_dtmStart = DateTime.Now;

            SqlConnection v_conn = new SqlConnection(p_strConnStr);
            SqlCommand v_cmd = new SqlCommand();
            int v_iResult = -5;

            try
            {
                PrepareCommand(v_cmd, v_conn, (SqlTransaction)null, p_strStoreName, p_arrSQLParameter);

                // Execute Sql Command
                v_iResult = v_cmd.ExecuteNonQuery();
                v_cmd.Parameters.Clear();
            }

            catch (Exception)
            {
                throw;
            }

            finally
            {
                if (v_conn.State == ConnectionState.Open)
                    v_conn.Close();
                v_cmd.Dispose();
            }

            DateTime? v_dtmEnd = DateTime.Now;
            TimeSpan v_tsElapsed = v_dtmEnd.Value - v_dtmStart.Value;
            if (v_tsElapsed.TotalMilliseconds >= 1000)
                CLogger.Trace("CSqlHelper", "ExecuteNonQuery", "Store: " + p_strStoreName + " execute " + v_tsElapsed.TotalSeconds.ToString("###,###0.######"));

            return v_iResult;
        }

        private static int ExecuteNonQuery(SqlConnection p_objConn, SqlTransaction p_objTrans,
            string p_strStoreName, params SqlParameter[] p_arrSQLParameter)
        {
            DateTime? v_dtmStart = DateTime.Now;

            SqlCommand v_cmd = new SqlCommand();
            int v_iResult = -5;

            try
            {
                PrepareCommand(v_cmd, p_objConn, p_objTrans, p_strStoreName, p_arrSQLParameter);

                // Execute Sql Command
                v_iResult = v_cmd.ExecuteNonQuery();
                v_cmd.Parameters.Clear();
            }

            catch (Exception)
            {
                throw;
            }

            finally
            {
                v_cmd.Dispose();
            }

            DateTime? v_dtmEnd = DateTime.Now;
            TimeSpan v_tsElapsed = v_dtmEnd.Value - v_dtmStart.Value;
            if (v_tsElapsed.TotalMilliseconds >= 1000)
                CLogger.Trace("CSqlHelper", "ExecuteNonQuery", "Store: " + p_strStoreName + " execute " + v_tsElapsed.TotalSeconds.ToString("###,###0.######"));

            return v_iResult;
        }

		public static object ExecuteScalar(string p_strConnStr, string p_strStoreName, params SqlParameter[] p_arrSQLParameter)
        {
            DateTime v_dtmStart = DateTime.Now;

            SqlConnection v_conn = new SqlConnection(p_strConnStr);
            SqlCommand v_cmd = new SqlCommand();
            object v_objResult = null;

            try
            {
                PrepareCommand(v_cmd, v_conn, (SqlTransaction)null, p_strStoreName, p_arrSQLParameter);
                // Execute Sql Command
                v_objResult = v_cmd.ExecuteScalar();
                v_cmd.Parameters.Clear();
            }

            catch (Exception)
            {
                throw;
            }

            finally
            {
                if (v_conn.State == ConnectionState.Open)
                    v_conn.Close();
                v_cmd.Dispose();
            }

            DateTime v_dtmEnd = DateTime.Now;
            TimeSpan v_tsElapsed = v_dtmEnd - v_dtmStart;
            if (v_tsElapsed.TotalMilliseconds >= 1000)
                CLogger.Trace("CSqlHelper", "ExecuteScalar", "Store: " + p_strStoreName + " execute " + v_tsElapsed.TotalSeconds.ToString("###,###0.######"));

            return v_objResult;
        }

        private static object ExecuteScalar(SqlConnection p_conn, SqlTransaction p_trans, string p_strStoreName,
            params SqlParameter[] p_arrSQLParameter)
        {
            DateTime v_dtmStart = DateTime.Now;

            SqlCommand v_cmd = new SqlCommand();
            object v_objResult = null;

            try
            {
                PrepareCommand(v_cmd, p_conn, p_trans, p_strStoreName, p_arrSQLParameter);
                // Execute Sql Command
                v_objResult = v_cmd.ExecuteScalar();
                v_cmd.Parameters.Clear();
            }

            catch (Exception)
            {
                throw;
            }

            finally
            {
                v_cmd.Dispose();
            }

            DateTime v_dtmEnd = DateTime.Now;
            TimeSpan v_tsElapsed = v_dtmEnd - v_dtmStart;
            if (v_tsElapsed.TotalMilliseconds >= 1000)
                CLogger.Trace("CSqlHelper", "ExecuteScalar", "Store: " + p_strStoreName + " execute " + v_tsElapsed.TotalSeconds.ToString("###,###0.######"));

            return v_objResult;
        }

        private static void FillDataSet(string p_strConnStr, DataSet p_dsData, string p_strStoreName, params SqlParameter[] p_arrSQLParameter)
        {
            using SqlConnection v_conn = new SqlConnection(p_strConnStr);
            using SqlCommand v_cmd = new SqlCommand();
            using SqlDataAdapter v_da = new SqlDataAdapter(v_cmd);

            PrepareCommand(v_cmd, v_conn, (SqlTransaction)null, p_strStoreName, p_arrSQLParameter);
            v_da.Fill(p_dsData);
        }

        private static void FillDataTable(string p_strConnStr, DataTable p_dtData, string p_strStoreName, params SqlParameter[] p_arrSQLParameter)
        {
            DateTime v_dtmStart = DateTime.Now;

            SqlConnection v_conn = new SqlConnection(p_strConnStr);
            SqlCommand v_cmd = new SqlCommand();
            SqlDataAdapter v_da = new SqlDataAdapter(v_cmd);

            try
            {
                PrepareCommand(v_cmd, v_conn, (SqlTransaction)null, p_strStoreName, p_arrSQLParameter);
                v_da.Fill(p_dtData);
            }

            catch (Exception)
            {
                throw;
            }

            finally
            {
                if (v_conn.State == ConnectionState.Open)
                    v_conn.Close();
                v_cmd.Dispose();
                v_da.Dispose();
            }

            DateTime v_dtmEnd = DateTime.Now;
            TimeSpan v_tsElapsed = v_dtmEnd - v_dtmStart;
            if (v_tsElapsed.TotalMilliseconds >= 1000)
                CLogger.Trace("CSqlHelper", "FillDataTable", "Store: " + p_strStoreName + " execute " + v_tsElapsed.TotalSeconds.ToString("###,###0.######"));
        }

        private static void FillDataTable(SqlConnection p_conn, SqlTransaction p_trans,
            DataTable p_dtData, string p_strStoreName, params SqlParameter[] p_arrSQLParameter)
        {
            DateTime v_dtmStart = DateTime.Now;

            SqlCommand v_cmd = new SqlCommand();
            SqlDataAdapter v_da = new SqlDataAdapter(v_cmd);

            try
            {
                PrepareCommand(v_cmd, p_conn, p_trans, p_strStoreName, p_arrSQLParameter);
                v_da.Fill(p_dtData);
            }

            catch (Exception)
            {
                throw;
            }

            finally
            {
                v_cmd.Dispose();
                v_da.Dispose();
            }

            DateTime v_dtmEnd = DateTime.Now;
            TimeSpan v_tsElapsed = v_dtmEnd - v_dtmStart;
            if (v_tsElapsed.TotalMilliseconds >= 1000)
                CLogger.Trace("CSqlHelper", "FillDataTable", "Store: " + p_strStoreName + " execute " + v_tsElapsed.TotalSeconds.ToString(CConfig.Number_Format_String));
        }

        private static void PrepareCommand(SqlCommand p_cmd, SqlConnection p_conn,
            SqlTransaction p_trans, string p_strSPName, SqlParameter[] p_arrSQLParameter)
        {
            //if the provided connection is not open, we will open it
            if (p_conn.State != ConnectionState.Open)
            {
                p_conn.Open();
            }

            //associate the connection with the command
            p_cmd.Connection = p_conn;

            //set the command text (stored procedure name or SQL statement)
            p_cmd.CommandText = p_strSPName;

            //if we were provided a transaction, assign it.
            if (p_trans != null)
            {
                p_cmd.Transaction = p_trans;
            }

            //set the command type
            p_cmd.CommandType = CommandType.StoredProcedure;

            //attach the command parameters if they are provided
            if (p_arrSQLParameter != null)
            {
                AttachParameters(p_cmd, p_arrSQLParameter);
            }
        }

        #endregion

    }
}
