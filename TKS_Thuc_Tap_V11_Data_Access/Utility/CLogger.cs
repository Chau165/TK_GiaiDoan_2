using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility
{
    public enum EFileType
    {
        /// <summary>
        /// Định dạng file kiểu text
        /// </summary>
        Text,
        /// <summary>
        /// Định dạng file kiểu XML
        /// </summary>
        XML
    }

    /// <summary>
    /// Logger Information
    /// </summary>
    public class CLogger
    {
        public static string User = "";
        public static string File_Name = "";  
        public static bool Enable_Trace = true;
        public static bool Enable_Error = true;
        public static bool Enable_Warning = true;
        public static bool Enable_Debug = true;
        public static EFileType File_Type = EFileType.Text;

        /// <summary>
        /// Update extend format FileName is log format
        /// </summary>
        /// <returns>String</returns>
        private static string Update_File_Name(string p_strFileName)
        {
            return p_strFileName.Replace(".log", "") + DateTime.Now.ToString("yyyyMMdd") + ".log";
        }

        public static void Write_To_File(string p_strMode, string p_strObjectName, string p_strFunctionName,
            string p_strContent)
        {
            string v_strFileExtend = "";
            TextWriter v_tw = null;
            CLog v_objData = new CLog();

            // Get File Name
            v_strFileExtend = Update_File_Name(File_Name);

            if (!File.Exists(v_strFileExtend))
            {
                string? v_strDir;
                if (v_strFileExtend.LastIndexOf("\\") >= 0)
                {
                    v_strDir = v_strFileExtend.Substring(0, v_strFileExtend.LastIndexOf("\\"));
                }
                else
                {
                    v_strDir = Path.GetDirectoryName(v_strFileExtend);
                }
                if (!string.IsNullOrEmpty(v_strDir)) Directory.CreateDirectory(v_strDir);
            } // End if

            try
            {
                v_tw = new StreamWriter(v_strFileExtend, File.Exists(v_strFileExtend));

                // Asign value to log object
                v_objData.Content = p_strContent;
                v_objData.Date = DateTime.Now.ToString("dd/MM/yyyy");
                v_objData.FunctionName = p_strFunctionName;
                v_objData.Mode = p_strMode;
                v_objData.ObjectName = p_strObjectName;
                v_objData.Time = DateTime.Now.ToString("hh:mm:ss");
                v_objData.User = User;

                // write data to log
                if (File_Type == EFileType.Text)
                    v_objData.WriteTextFile(v_tw);

                if (File_Type == EFileType.XML)
                    v_objData.WriteXmlFile(v_tw);
            }
            catch (Exception)
            {
            }
            finally
            {
                if (v_tw != null)
                    v_tw.Close();
            }
        }

        /// <summary>
        /// Trace log
        /// </summary>
        /// <param name="p_strObjectName">Object Name</param>
        /// <param name="p_strFunctionName">FunctionName</param>
        /// <param name="p_strContent">Content</param>
        public static void Trace(string p_strObjectName, string p_strFunctionName, string p_strContent)
        {
            if (Enable_Trace == true)
                Write_To_File("Trace", p_strObjectName, p_strFunctionName, p_strContent);
        }

        /// <summary>
        /// Debug log
        /// </summary>
        /// <param name="p_strObjectName">Object Name</param>
        /// <param name="p_strFunctionName">FunctionName</param>
        /// <param name="p_strContent">Content</param>
        public static void Debug(string p_strObjectName, string p_strFunctionName, string p_strContent)
        {
            if (Enable_Debug == true)
                Write_To_File("Debug", p_strObjectName, p_strFunctionName, p_strContent);
        }

        /// <summary>
        /// Debug log
        /// </summary>
        /// <param name="p_strObjectName">Object Name</param>
        /// <param name="p_strFunctionName">FunctionName</param>
        /// <param name="p_strContent">Content</param>
        public static void Error(string p_strObjectName, string p_strFunctionName, string p_strContent)
        {
            if (Enable_Error == true)
                Write_To_File("Error", p_strObjectName, p_strFunctionName, p_strContent);
        }

        /// <summary>
        /// Warning log
        /// </summary>
        /// <param name="p_strObjectName">Object Name</param>
        /// <param name="p_strFunctionName">FunctionName</param>
        /// <param name="p_strContent">Content</param>
        public static void Warning(string p_strObjectName, string p_strFunctionName, string p_strContent)
        {
            if (Enable_Warning == true)
                Write_To_File("Warning", p_strObjectName, p_strFunctionName, p_strContent);
        }
    }
}
