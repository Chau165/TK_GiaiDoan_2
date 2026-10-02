using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Configuration;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.DataLayer
{
    public class CSqlHelperParameterCache
    {
        //*********************************************************************
        //
        // Since this class provides only static methods, make the default constructor private to prevent 
        // instances from being created with "new SqlHelperParameterCache()".
        //
        //*********************************************************************

        private CSqlHelperParameterCache() { }

        private static Hashtable g_dicParamCache = Hashtable.Synchronized(new Hashtable());

        //*********************************************************************
        //
        // resolve at run time the appropriate set of SqlParameters for a stored procedure
        // 
        // param name="connectionString" a valid connection string for a SqlConnection 
        // param name="spName" the name of the stored procedure 
        // param name="includeReturnValueParameter" whether or not to include their return value parameter 
        //
        //*********************************************************************

        private static SqlParameter[] DiscoverSpParameterSet(string p_connectionString, string p_spName, bool p_bIncludeReturnValueParameter)
        {
            SqlConnection v_cn = new SqlConnection(p_connectionString);
            SqlCommand v_cmd = new SqlCommand(p_spName, v_cn);
            SqlParameter[] v_arrDiscoveredParameters;

            try
            {
                v_cn.Open();
                v_cmd.CommandType = CommandType.StoredProcedure;

                SqlCommandBuilder.DeriveParameters(v_cmd);

                if (!p_bIncludeReturnValueParameter)
                {
                    v_cmd.Parameters.RemoveAt(0);
                }

                v_arrDiscoveredParameters = new SqlParameter[v_cmd.Parameters.Count]; ;

                v_cmd.Parameters.CopyTo(v_arrDiscoveredParameters, 0);
            }

            catch (Exception)
            {
                throw;
            }

            finally
            {
                v_cn.Close();
                v_cmd.Dispose();
            }

            return v_arrDiscoveredParameters;
        }

        private static SqlParameter[] CloneParameters(SqlParameter[] p_arrOriginalParameters)
        {
            //deep copy of cached SqlParameter array
            SqlParameter[] v_arrClonedParameters = new SqlParameter[p_arrOriginalParameters.Length];

            for (int v_iIndex = 0, v_iCount = p_arrOriginalParameters.Length; v_iIndex < v_iCount; v_iIndex++)
            {
                v_arrClonedParameters[v_iIndex] = (SqlParameter)((ICloneable)p_arrOriginalParameters[v_iIndex]).Clone();
            }

            return v_arrClonedParameters;
        }

        //*********************************************************************
        //
        // add parameter array to the cache
        //
        // param name="connectionString" a valid connection string for a SqlConnection 
        // param name="commandText" the stored procedure name or T-SQL command 
        // param name="commandParameters" an array of SqlParamters to be cached 
        //
        //*********************************************************************

        public static void CacheParameterSet(string connectionString, string commandText, params SqlParameter[] commandParameters)
        {
            string v_hashKey = connectionString + ":" + commandText;

            g_dicParamCache[v_hashKey] = commandParameters;
        }

        //*********************************************************************
        //
        // Retrieve a parameter array from the cache
        // 
        // param name="connectionString" a valid connection string for a SqlConnection 
        // param name="commandText" the stored procedure name or T-SQL command 
        // returns an array of SqlParamters
        //
        //*********************************************************************

        public static SqlParameter[] GetCachedParameterSet(string connectionString, string commandText)
        {
            string v_hashKey = connectionString + ":" + commandText;

            SqlParameter[] v_arrCachedParameters = (SqlParameter[])g_dicParamCache[v_hashKey];

            if (v_arrCachedParameters == null)
            {
                return null;
            }
            else
            {
                return CloneParameters(v_arrCachedParameters);
            }
        }

        //*********************************************************************
        //
        // Retrieves the set of SqlParameters appropriate for the stored procedure
        // 
        // This method will query the database for this information, and then store it in a cache for future requests.
        // 
        // param name="connectionString" a valid connection string for a SqlConnection 
        // param name="spName" the name of the stored procedure 
        // returns an array of SqlParameters
        //
        //*********************************************************************

        public static SqlParameter[] GetSpParameterSet(string connectionString, string spName)
        {
            return GetSpParameterSet(connectionString, spName, false);
        }

        //*********************************************************************
        //
        // Retrieves the set of SqlParameters appropriate for the stored procedure
        // 
        // This method will query the database for this information, and then store it in a cache for future requests.
        // 
        // param name="connectionString" a valid connection string for a SqlConnection 
        // param name="spName" the name of the stored procedure 
        // param name="includeReturnValueParameter" a bool value indicating whether the return value parameter should be included in the results 
        // returns an array of SqlParameters
        //
        //*********************************************************************

        public static SqlParameter[] GetSpParameterSet(string connectionString, string spName, bool includeReturnValueParameter)
        {
            string v_hashKey = connectionString + ":" + spName;
            if (includeReturnValueParameter)
            {
                v_hashKey = v_hashKey + ":include ReturnValue Parameter";
            }

            SqlParameter[] v_arrCachedParameters;

            v_arrCachedParameters = (SqlParameter[])g_dicParamCache[v_hashKey];

            if (v_arrCachedParameters == null)
            {
                v_arrCachedParameters = (SqlParameter[])(g_dicParamCache[v_hashKey] = DiscoverSpParameterSet(connectionString, spName, includeReturnValueParameter));
            }

            return CloneParameters(v_arrCachedParameters);
        }
    }
}
