using Common.Interface.ITopo;
using Common.ModeBaseStructure.Group;

namespace Common.Model.DA
{
    /// <summary>
    /// 空间参数状态变量
    /// </summary>
    public class StateSpaceVariableInfo : StateVariableInfo
    {
        static byte version = 0;
        /// <summary>
        /// 特征点分组
        /// </summary>
        public ModelSpaceGroup ModelSpaceGroup { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<List<int>> SpaceGroup { get; set; }
        private int nvar = 0;
        public void Prepost(IHyModelTopo topo)
        {
            ModelSpaceGroup.Prepost(topo);
            foreach (var item in ModelSpaceGroup.AreaIDs)
            {
                SpaceGroup.Add(item.Value);
                nvar += item.Value.Count;
            }
            SpaceGroup.Add(ModelSpaceGroup.DefaultIDS);
            nvar += ModelSpaceGroup.DefaultIDS.Count;
        }
        /// <summary>
        /// 计算模型网格相关状态变量的范围，如果为非网格相关变量需要单独指定
        /// </summary>
        /// <param name="topo">模型topo</param>
        public override void VariableRange(IHyModelTopo topo)
        {
            Prepost(topo);
            base.VariableRange(topo);
            if (VariableType == Enums.DAVariableType.Parameter)
            {
                Range = ModelSpaceGroup.AreaIDs.Count;
            }
        }
        /// <summary>
        /// 同化分组变量转换为模型参数变量
        /// </summary>
        /// <param name="groupVariable">分组变量</param>
        /// <returns>模型参数变量</returns>
        public double[] ToModelVariable(double[] groupVariable)
        {
            double[] rval = new double[nvar];
            int k = 0;
            for (int i = 0; i < SpaceGroup.Count; i++)
            {
                for(int j = 0;j < SpaceGroup[i].Count;j++,k++)
                {
                    rval[k] = groupVariable[i];
                }
            }
            return rval;
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
                    ModelSpaceGroup.InBianary(rd);
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

                ModelSpaceGroup.OutBianary(wr);
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
                    else if (cmd.IsCommond("Group"))
                    {
                        List<TxtFileString> tline = lines.GetCommondStrings(cmd, ref i, i + 1);
                        ModelSpaceGroup.InString(ref tline, par);
                    }
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
                rval.Add($"{spaceStr1}[Group]");
                rval.AddRange(ModelSpaceGroup.ToStrings(level + 1, par, cmd));
                rval.Add($"{spaceStr1}[/Group]");

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
