using Common.Enums;
using Common.Interface;
using System.Xml.Linq;
using System;

namespace Common.Model.DA
{
    /// <summary>
    /// 观测点信息
    /// </summary>
    public class ObserveInfo : IMyIO
    {
        static byte version = 0;
        /// <summary>
        /// 时间序列名称
        /// </summary>
        public string SerialName { get; set; }
        /// <summary>
        /// 观测的标准差
        /// </summary>
        public double Observe_Error { get; set; }
        #region MYIO
        /// <summary>
        /// 输入二进制
        /// </summary>
        /// <param name="rd">文件流</param>
        public virtual void InBianary(BinaryReader rd)
        {
            try
            {
                byte tv = 1;
                tv = rd.ReadByte();
                if (tv <= 1)
                {
                    SerialName = rd.ReadNullTerminatedString();
                    Observe_Error = rd.ReadDouble();
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
        /// <summary>
        /// 输出二进制格式
        /// </summary>
        /// <param name="wr">文件流</param>
        public virtual void OutBianary(BinaryWriter wr)
        {
            try
            {
                wr.Write(version);
                wr.WriteString(SerialName);
                wr.WriteDouble(Observe_Error);
            }
            catch (Exception ex)
            {

                Common.Blog.BlogErrorInfo(ex.ToString());
            }
        }
        /// <summary>
        /// 从文本读取参数
        /// </summary>
        /// <param name="lines">描述字符串</param>
        /// <param name="par">参数</param>
        public virtual void InString(ref List<TxtFileString> lines, object par = null)
        {
            int i = 0;
            try
            {
                string cmd;
                byte tv = 1;
                for (; i < lines.Count; i++)
                {
                    TxtFileString line = lines[i];
                    List<string> strs = line.SubStrings();
                    if (strs.Count <= 0) continue;
                    cmd = strs[0];
                    if (cmd.IsCommond("Version")) { if (strs.Count > 1) _ = byte.TryParse(strs[1], out tv); }
                    else if (cmd.IsCommond("Name")) { if (strs.Count > 1) SerialName = strs[1]; }
                    else if (cmd.IsCommond("Error")) { if (strs.Count > 1 && double.TryParse(strs[1], out double td)) Observe_Error = td; }
                }
            }
            catch (Exception ex)
            {
                Common.Blog.BlogErrorInfo(ex.ToString(), lines[i]);
            }
        }

        /// <summary>
        /// 写文本参数
        /// </summary>
        /// <param name="level">level</param>
        /// <param name="par">参数</param>
        /// <param name="cmd">名称</param>
        /// <returns>文本描述</returns>
        public virtual List<string> ToStrings(int level = 0, object par = null, string cmd = null)
        {
            List<string> rval = new();

            try
            {
                string spaceStr = level.GetCommondLevelString();
                string spaceStr1 = (level + 1).GetCommondLevelString();
                string spaceStr2 = (level + 2).GetCommondLevelString();
                if (cmd != null) rval.Add($"{spaceStr}[{cmd}]");
                else spaceStr1 = spaceStr;
                rval.Add($"{spaceStr1}[Version]\t{version}");

                rval.Add($"{spaceStr1}[Name]\t{SerialName}");
                rval.Add($"{spaceStr1}[Error]\t{Observe_Error}");

                if (cmd != null) rval.Add($"{spaceStr}[/{cmd}]");
            }
            catch (Exception ex)
            {

                Common.Blog.BlogErrorInfo(ex.ToString());
            }

            return rval;
        }
        #endregion

    }
}
