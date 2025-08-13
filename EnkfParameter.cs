using Common.Enums;
using Common.Interface.IHydraulicModel;
using Common.Interface;
using Common.Interface.ITopo;
using Common.ModeBaseStructure.TopoPosition;
using Common.Model.Topo.Tool;
using Common.ModelParameter;

namespace Common.Model.DA
{
    /// <summary>
    /// 集合卡尔曼滤波模型参数
    /// </summary>
    public class EnkfParameter : ModelParameterBase
    {
        /// <summary>
        /// 版本号
        /// </summary>
        static byte version = 0;
        /// <summary>
        /// 数据集合的个数
        /// </summary>
        public int Enkf_N { get; set; } = 100;
        /// <summary>
        /// 观测变量[key] 变量名称 [value] 观测点和观测序列
        /// </summary>
        public Dictionary<VaraiblePosition, ObserveInfo> Obv_Serial { get; set; } = new();
        /// <summary>
        /// 模型误差（均方差）
        /// </summary>
        public Dictionary<string, double> Model_err { get; set; } = new();
        /// <summary>
        /// 状态变量
        /// </summary>
        public List<StateVariableInfo> StateVariable { get; set; } = new();
        /// <summary>
        /// 观测值的时间间隔，也就是同化的时间间隔
        /// </summary>
        public double Obv_timeStep { get; set; }
        /// <summary>
        /// 预报计算的时间间隔>Obv_timeStep
        /// </summary>
        public double Pre_timeStep { get; set; }
        /// <summary>
        /// 预报的时间长度
        /// </summary>
        public double Pre_timeLong { get; set; }
        /// <summary>
        /// 开始预报计算的时间
        /// </summary>
        public double Pre_StartTime { get; set; }
        /// <summary>
        /// 预报的结束时间
        /// </summary>
        public double Pre_EndTime { get; set; }=double.MaxValue;
        /// <summary>
        /// 模型是否从热启动开始预报
        /// </summary>
        public bool IsHotStartPrediction { get; set; } = false;
        /// <summary>
        /// 如果不是热启动预报，需要预处理计算的时间
        /// </summary>
        public double Pre_predictionTime { get; set; } = 0;
        /// <summary>
        /// 预报过程是否结束
        /// </summary>
        public bool IsPredictionOver { get { return Pre_StartTime + Pre_timeLong >= Pre_EndTime; } }
        /// <summary>
        /// 观测点的个数
        /// </summary>
        /// <returns>观测点的个数</returns>
        public int ObservaiableNumber()
        {
            int m = Obv_Serial.Count;
            return m;
        }
        /// <summary>
        /// 获得变量在转态变量中的开始缩影
        /// </summary>
        /// <param name="variableName">变量名称</param>
        /// <returns>缩影编号</returns>
        public int GetVariableStartIndex(string variableName)
        {
            for (int i = 0; i < StateVariable.Count; i++)
            {
                if (StateVariable[i].Name.ToUpper() == variableName.ToUpper()) return StateVariable[i].StartIndex;
            }

            return -1;
        }
        /// <summary>
        /// 参数前处理
        /// </summary>
        /// <param name="model">模型</param>
        /// <returns>是否成功</returns>
        public override bool Prepost(IHydraulicModel model)
        {
            IHyModelTopo topo = model.HyModelTopo;
            foreach (var item in StateVariable)
            {
                item.VariableRange(topo);
            }
            foreach (var item in Obv_Serial)
            {
                IRiverNetworkTopo rtopo = topo.ToRiverNetworkTopo();
                if (rtopo != null) item.Key.Prepost(rtopo);
            }

            foreach (var item in StateVariable)
            {
                if (model is IParameterIndex par && (item.VariableType == DAVariableType.Foreast || item.VariableType == DAVariableType.Parameter))
                {
                    item.ParameterIndex = par.GetParameterIndex(item.Name);
                }
                else if (item.VariableType == DAVariableType.Boundary)
                {
                    var serial = model.GetSerial(item.Name);
                    if (serial is IParameterIndex bndPar && item is StateBoundaryVariableInfo bnd)
                    {
                        item.ParameterIndex = bndPar.GetParameterIndex(bnd.ParameterName);
                        if (item.ParameterIndex == -1) 
                        {
                            model.Blog.BlogErrorInfo($"boundary {bndPar.GetType()} can not set variable {bnd.ParameterName} as state variable");
                        }
                        bnd.Boundary = bndPar;
                    }
                    else
                    {
                        if (serial as IParameterIndex == null) { model.Blog.BlogErrorInfo($"boundary parameter{item.Name} as state variable, the serial object must have interface IParameterIndex"); }
                    }
                }
            }
            return base.Prepost(model);
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
                byte tv = 1;
                tv = rd.ReadByte();
                if (tv <= 1)
                {
                    Enkf_N = rd.ReadInt();
                    Pre_StartTime = rd.ReadDouble();
                    Pre_EndTime = rd.ReadDouble();
                    Pre_timeStep = rd.ReadDouble();
                    Pre_timeLong = rd.ReadDouble();
                    Obv_timeStep = rd.ReadDouble();
                    IsHotStartPrediction = (bool)rd.ReadBool1();
                    Pre_predictionTime = rd.ReadDouble();
                    int count = rd.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        VaraiblePosition varaiblePosition = new();
                        varaiblePosition.InBianary(rd);
                        ObserveInfo obv = new();
                        obv.InBianary(rd);
                        Obv_Serial.Add(varaiblePosition, obv);
                    }
                    count = rd.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        StateVariableInfo info = rd.ReadObject() as StateVariableInfo;
                        StateVariable.Add(info);
                    }
                }
                base.InBianary(rd);
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
                wr.Write(version);
                wr.WriteInt(Enkf_N);
                wr.WriteDouble(Pre_StartTime);
                wr.WriteDouble(Pre_EndTime);
                wr.WriteDouble(Pre_timeStep);
                wr.WriteDouble(Pre_timeLong);
                wr.WriteDouble(Obv_timeStep);
                wr.WriteBool1(IsHotStartPrediction);
                wr.WriteDouble(Pre_predictionTime);
                wr.WriteInt(Obv_Serial.Count);
                foreach (var item in Obv_Serial)
                {
                    item.Key.OutBianary(wr);
                    item.Value.OutBianary(wr);
                }
                wr.WriteInt(StateVariable.Count);
                for (int i = 0; i < StateVariable.Count; i++)
                {
                    wr.WriteObject(StateVariable[i]);
                }
                base.OutBianary(wr);
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
        public override void InString(ref List<TxtFileString> lines, object par = null)
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
                    if (cmd.IsCommond("Version")) { _ = byte.TryParse(strs[1], out tv); }
                    else if (cmd.IsCommond(new string[] { "ENKF_N" })) { if (strs.Count > 1 && strs[1].ToInt(out int tint)) { Enkf_N = tint; } }
                    else if (cmd.IsCommond(new string[] { "PreStartTime" })) { if (strs.Count >= 2) { strs.RemoveAt(0); Pre_StartTime = StringTool.GetDateTime(strs); } }
                    else if (cmd.IsCommond(new string[] { "PreEndTime" })) { if (strs.Count >= 2) { strs.RemoveAt(0); Pre_EndTime = StringTool.GetDateTime(strs); } }
                    else if (cmd.IsCommond(new string[] { "PretimeStep" })) { if (strs.Count >= 2 && strs[1].ToDouble(out double td)) { Pre_timeStep = td; } }
                    else if (cmd.IsCommond(new string[] { "Pretimelong" })) { if (strs.Count >= 2 && strs[1].ToDouble(out double td)) { Pre_timeLong = td; } }
                    else if (cmd.IsCommond(new string[] { "ObvTimeStep" })) { if (strs.Count >= 2 && strs[1].ToDouble(out double td)) { Obv_timeStep = td; } }
                    else if (cmd.IsCommond(new string[] { "IsHotStartPre" })) { if (strs.Count >= 2 && bool.TryParse(strs[1], out bool td)) { IsHotStartPrediction = td; } }
                    else if (cmd.IsCommond(new string[] { "PrePredictionTime" })) { if (strs.Count >= 2) { Pre_predictionTime = strs[1].AsDouble(); } }
                    else if (cmd.IsCommond(new string[] { "Obvervation" }))
                    {
                        List<TxtFileString> tline = lines.GetCommondStrings(cmd, ref i, i + 1);
                        VaraiblePosition varaiblePosition = new();
                        ObserveInfo observeInfo = new();
                        List<TxtFileString> strings = new();
                        for (int j = 0; j < tline.Count; j++)
                        {
                            line = tline[j];
                            strs = line.SubStrings();
                            cmd = strs[0];

                            if (cmd.IsCommond("SerialName") && strs.Count > 1) { observeInfo.SerialName = strs[1]; }
                            else if (cmd.IsCommond("ObvErr") && strs.Count > 1) { if (double.TryParse(strs[1], out double td)) { observeInfo.Observe_Error = td; } }
                            else if (cmd.IsCommond("VariablePos"))
                            {
                                List<TxtFileString> ttline = tline.GetCommondStrings(cmd, ref j, j + 1);
                                varaiblePosition.InString(ref ttline);
                            }
                            else
                            {
                                strings.Add(tline[j]);
                            }
                        }
                        if (strings.Count > 0) varaiblePosition.InString(ref strings);

                        Obv_Serial[varaiblePosition] = observeInfo;
                    }
                    else if (cmd.IsCommond(new string[] { "StateVariable" }))
                    {
                        List<TxtFileString> tline = lines.GetCommondStrings(cmd, ref i, i + 1);
                        StateVariableInfo stateVariableInfo = new() { VariableType = DAVariableType.Foreast };
                        stateVariableInfo.InString(ref tline);
                        StateVariable.Add(stateVariableInfo);
                    }
                    else if (cmd.IsCommond(new string[] { "StateSpaceVariable" }))
                    {
                        List<TxtFileString> tline = lines.GetCommondStrings(cmd, ref i, i + 1);
                        StateSpaceVariableInfo stateVariableInfo = new() { VariableType = DAVariableType.Parameter };
                        stateVariableInfo.InString(ref tline);
                        StateVariable.Add(stateVariableInfo);
                    }
                    else if (cmd.IsCommond(new string[] { "StateBoundaryVariable" }))
                    {
                        List<TxtFileString> tline = lines.GetCommondStrings(cmd, ref i, i + 1);
                        StateBoundaryVariableInfo stateVariableInfo = new() { VariableType = DAVariableType.Parameter };
                        stateVariableInfo.InString(ref tline);
                        StateVariable.Add(stateVariableInfo);
                    }
                }
                base.InString(ref lines, par);
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

                rval.Add($"{spaceStr1}[ENKF_N]\t{Enkf_N}");
                rval.Add($"{spaceStr1}[PreStartTime]\t{Common.Date(Pre_StartTime)}");
                if (Pre_EndTime != double.MaxValue)
                {
                    rval.Add($"{spaceStr1}[PreEndTime]\t{Common.Date(Pre_EndTime)}");
                }
                rval.Add($"{spaceStr1}[PretimeStep]\t{Pre_timeStep}");
                rval.Add($"{spaceStr1}[Pretimelong]\t{Pre_timeLong}");
                rval.Add($"{spaceStr1}[ObvTimeStep]\t{Obv_timeStep}");
                rval.Add($"{spaceStr1}[IsHotStartPre]\t{IsHotStartPrediction}");
                rval.Add($"{spaceStr1}[PrePredictionTime]\t{Pre_predictionTime}");

                rval.Add($"{spaceStr1}[Obvervation]");
                foreach (var item in Obv_Serial)
                {
                    rval.Add($"{spaceStr2}[VariablePos]");
                    rval.AddRange(item.Key.ToStrings(level + 1));
                    rval.Add($"{spaceStr2}[/VariablePos]");
                    rval.Add($"{spaceStr2}[SerialName]\t{item.Value.SerialName}");
                    rval.Add($"{spaceStr2}[ObvErr]\t{item.Value.Observe_Error}");
                }
                rval.Add($"{spaceStr1}[/Obvervation]");

                foreach (var item in StateVariable)
                {
                    if (item is StateBoundaryVariableInfo sbv)
                    {
                        rval.AddRange(sbv.ToStrings(level + 1, par, "StateBoundaryVariable"));
                    }
                    else if (item is StateSpaceVariableInfo spv)
                    {
                        rval.AddRange(spv.ToStrings(level + 1, par, "StateSpaceVariable"));
                    }
                    else if (item is StateVariableInfo svi)
                    {
                        rval.AddRange(svi.ToStrings(level + 1, par, "StateVariable"));
                    }
                }

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
