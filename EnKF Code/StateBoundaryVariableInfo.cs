using Common.Enums;
using Common.Interface;

namespace Common.Model.DA
{
    /// <summary>
    /// 边界条件参数状态变量
    /// </summary>
    public class StateBoundaryVariableInfo: StateVariableInfo
    {
        static byte version = 0;
        /// <summary>
        /// 边界条件序列
        /// </summary>
        public IParameterIndex Boundary { get; set; }
        /// <summary>
        /// 参数名称
        /// </summary>
        public string ParameterName { get; set; }=null;
        public StateBoundaryVariableInfo()
        {
            VariableType = DAVariableType.Boundary;
            Range = 1;
        }
        #region MYIO
        /// <summary>
        /// 输入二进制
        /// </summary>
        /// <param name="rd">文件流</param>
        public override void InBianary(BinaryReader rd)
        {
            try
            {
                base.InBianary(rd);
                byte tv = 1;
                tv = rd.ReadByte();
                if (tv <= 1)
                {
                    ParameterName = rd.ReadNullTerminatedString();
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
        public override void OutBianary(BinaryWriter wr)
        {
            try
            {
                base.OutBianary(wr);
                wr.Write(version);
              wr.WriteString(ParameterName);
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
        /// <param name="par">参数</param
        public override void InString(ref List<TxtFileString> lines, object par = null)
        {
            int i = 0;
            try
            {
                string cmd;
                byte tv = 1;
                List<TxtFileString> strings = new();
                for (; i < lines.Count; i++)
                {
                    TxtFileString line = lines[i];
                    List<string> strs = line.SubStrings();
                    if (strs.Count <= 0) continue;
                    cmd = strs[0];
                    if (cmd.IsCommond("Version")) { if (strs.Count > 1) _ = byte.TryParse(strs[1], out tv); }
                    else if (cmd.IsCommond("ParameterName")) { if (strs.Count > 1) ParameterName = strs[1]; }
                    else
                    {
                        strings.Add(line);
                    }
                }
                base.InString(ref strings, par);
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
        public override List<string> ToStrings(int level = 0, object par = null, string cmd = null)
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

                rval.AddRange(base.ToStrings(level, par, cmd));
                rval.Add($"{spaceStr1}[ParameterName]\t{ParameterName}");

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
