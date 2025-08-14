using Common.Enums;
using Common.Interface;
using Common.Interface.IHydraulicModel;
using Common.Interface.ITopo;
using Common.Model.Topo.Tool;

namespace Common.Model.DA
{
    /// <summary>
    /// 同化变量在状态变量中的起始索引和范围
    /// </summary>
    public class StateVariableInfo : IMyIO
    {
        private static byte version = 0;
        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 模型参数索引，如果模型具有IParameter接口，否则为-1
        /// </summary>
        public int ParameterIndex { get; set; } = -1;
        /// <summary>
        /// 状态变量类型
        /// </summary>
        public DAVariableType VariableType { get; set; } = DAVariableType.Foreast;
        /// <summary>
        /// 变量在状态变量中的起始索引
        /// </summary>
        public int StartIndex { get; set; } = -1;
        /// <summary>
        /// 变量的大小
        /// </summary>
        public int Range { get; set; }=0;
    
        /// <summary>
        /// 变量的的topo属性
        /// </summary>
        public TopoAttributeType TopoAttribute { get; set; }= TopoAttributeType.Default;
        /// <summary>
        /// 预测的标准差
        /// </summary>
        public double Error { get; set; }
        /// <summary>
        /// 变量的最大取值
        /// </summary>
        public double Max { get; set; } = double.MaxValue;
        /// <summary>
        /// 变量的最小取值
        /// </summary>
        public double Min { get; set; }= double.MinValue;
        /// <summary>
        /// 是否将最优估计值复制给模型
        /// </summary>
        public bool IsResetToModel { get; set; } = true;
        /// <summary>
        /// 计算模型网格相关状态变量的范围，如果为非网格相关变量需要单独指定
        /// </summary>
        /// <param name="topo">模型topo</param>
        public virtual void VariableRange(IHyModelTopo topo)
        {
            if (VariableType == DAVariableType.Foreast|| VariableType == DAVariableType.Parameter) { Range = topo.GetTopoAttributeCount(TopoAttribute); }
        }
       
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
                    Name = rd.ReadNullTerminatedString();
                    TopoAttribute = rd.ReadEnum<TopoAttributeType>();
                    VariableType = rd.ReadEnum<DAVariableType>();
                    Error = rd.ReadDouble();
                    IsResetToModel = (bool)rd.ReadBool1();
                    Max = rd.ReadDouble();
                    Min = rd.ReadDouble();
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
                wr.WriteString(Name);
                wr.WriteEnum(TopoAttribute);
                wr.WriteEnum(VariableType);
                wr.WriteDouble(Error);
                wr.WriteBool1(IsResetToModel);
                wr.WriteDouble(Max);
                wr.WriteDouble(Min);
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
                    else if (cmd.IsCommond("Name")) { if (strs.Count > 1) Name = strs[1]; }
                    else if (cmd.IsCommond("VariablePosition")) { if (strs.Count > 1) TopoAttribute = EnumTool.GetEnumFromString<TopoAttributeType>(strs[1]); }
                    else if (cmd.IsCommond("DAVariableType")) { if (strs.Count > 1) VariableType = EnumTool.GetEnumFromString<DAVariableType>(strs[1]); }
                    else if (cmd.IsCommond("Error")) { if (strs.Count > 1 && strs[1].ToDouble( out double td)) Error = td; }
                    else if (cmd.IsCommond("IsResetToModel")) { if (strs.Count > 1 && strs[1].ToBool(out bool td)) IsResetToModel = td; }
                    else if (cmd.IsCommond("Max")) { if (strs.Count > 1 && strs[1].ToDouble(out double td)) Max = td; }
                    else if (cmd.IsCommond("Min")) { if (strs.Count > 1 && strs[1].ToDouble(out double td)) Min = td; }
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

                rval.Add($"{spaceStr1}[Name]\t{Name}");
                rval.Add($"{spaceStr1}[VariablePosition]\t{TopoAttribute}");
                rval.Add($"{spaceStr1}[DAVariableType]\t{VariableType}");
                rval.Add($"{spaceStr1}[Error]\t{Error}");


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
