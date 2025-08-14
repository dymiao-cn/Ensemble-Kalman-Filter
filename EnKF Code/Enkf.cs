//#define DAPARRELL
using Common.DB;
using Common.Global;
using Common.Interface;
using Common.Interface.IHydraulicModel;
using Common.Interface.IModel;
using Common.Interface.ITopo;
using Common.Interface.ModelBataBase;
using Common.ModeBaseStructure.TopoPosition;
using Common.Model.HyModel;
using Common.Model.Topo.Tool;
using Common.ModelParameter;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
///
namespace Common.Model.DA
{
    /// <summary>
    /// 集合卡尔曼滤波数据同化的基类
    /// </summary>
    public class Enkf : ModelBase, IExeModel
    {
        static byte version = 0;
        /// <summary>
        /// 模型名称
        /// </summary>
        public override string ModelName => "EnKfDA";

        /// <summary>
        /// 模型
        /// </summary>
        public IHydraulicModel PreModel { get; set; }
        /// <summary>
        /// 模型
        /// </summary>
        protected IHydraulicModel[] models;

        /// <summary>
        /// 数据同化参数
        /// </summary>
        public EnkfParameter Parameter { get; set; } = new();
        /// <summary>
        /// 状态变量个数
        /// </summary>
        protected int n;
        /// <summary>
        /// 集合个数
        /// </summary>
        protected int N;
        /// <summary>
        /// 观测变量个数
        /// </summary>
        protected int m;
        /// <summary>
        /// 日志
        /// </summary>
        public Blog Blog { get; set; }
        /// <summary>
        /// 观测序列
        /// </summary>
        protected List<ISeries> obv_Series = new();
        /// <summary>
        /// 观测序列编号对应的变量名称和模型索引编号
        /// </summary>
        private Dictionary<int, VaraiblePosition> obv_Index = new();

        //private double[] enkf_HMtx;  //[n*m] 稀疏矩阵，描述了观测点的估计值与计算点上值的关系，为了简化计算，我们可以假设观测点与计算点重合，且观测值和估计值的换算关系为1；
        protected int[] enkf_HIndex;   //[m] 
        protected double[] enkf_RMtx; //[m] 观测误差协方差
        protected double[] enkf_QMtx; //[n] 模型误差协方差
        protected double[,] enkf_PHMtx; //[n*m] P*H' 矩阵
        protected double[,] enkf_HPHMtx;//[m*m] H*P*H' 矩阵
        protected double[] enkf_HXMtx; //[m] H*X
        protected double[] enkf_HXAMtx; // [m] 集合平均 H*X 
        protected double[][] enkf_aVect; //估计状态变量集合[N][n]
        protected double[] enkf_aAVect; //估计状态变量集合的平均值，最优估计
        protected double[] enkf_obv;


        /// <summary>
        /// 记录模型预报之前的值与调整糙率必要预报值
        /// </summary>
        protected int[] RoughnessIndex;        
        protected double[] VariableSavepre;
        protected double[] Roughadj_obv;


        private ModelInitialParameter Intiialdata {  get; set; }

        /// <summary>
        /// 模型
        /// </summary>
        private Dictionary<string, IModelDataBase> Db { get; set; } = new();
        /// <summary>
        /// 输出的时间过程
        /// </summary>
        protected Dictionary<string, Dictionary<string, object>> OutTimeData { get; set; } = new();
        /// <summary>
        /// 输出的非时间过程
        /// </summary>
        protected Dictionary<string, Dictionary<string, object>> OutNoTimeData { get; set; } = new();
        /// <summary>
        /// 方案描述
        /// </summary>
        public string ProjectDescribe { get; set; }

        /// <summary>
        /// 运行模型
        /// </summary>
        /// <returns>是否成功</returns>
        public virtual bool Run()
        {
            Prepost();
#if DAPARRELL
            Parallel.Invoke(Common.ParallelOptions,
                  () => Predicion(),
                  () => DataAssimilation()
                  );
#else
            Predicion();
            Blog.LockBlogFile = true;
            DataAssimilation();
#endif
            do
            {
                Parameter.Pre_StartTime += Parameter.Pre_timeStep;
                try
                {
#if DAPARRELL
                    Parallel.Invoke(Common.ParallelOptions,
                          () => Predicion(),
                          () => DataAssimilation()
                          );
#else
                    Blog.BlogPromptInfo($"Start Prediction, current time{DateTime.Now}");
                    Predicion();
                    DataAssimilation();
#endif
                }
                catch (Exception)
                {
                    break;
                }

            } while (!Parameter.IsPredictionOver);

            if (Parameter.IsPredictionOver) Blog.BlogInfor("ENKF model is complete");
            else Blog.BlogInfor("ENKF model is over.Please check model, there may be some error");
            return false;
        }
        /// <summary>
        /// 模型前处理
        /// </summary>
        /// <returns></returns>
        public virtual bool Prepost()
        {
            try
            {
                PreModel.Blog = Blog;
                Parameter.Prepost(PreModel);
                SetStateVariableIndex();
                //便于程序编写            
                N = Parameter.Enkf_N;
                m = Parameter.ObservaiableNumber();
                n = GetVariableNumber();

                NewEnkfMatrix();
                
                int k = 0;
                foreach (var item in Parameter.Obv_Serial)
                {
                    obv_Series.Add(GlobalVariable.GetSeries(item.Value.SerialName));
                    enkf_RMtx[k] = item.Value.Observe_Error;

                    enkf_HIndex[k] = Parameter.GetVariableStartIndex(item.Key.VariableName) + item.Key.Index;
                    RoughnessIndex[k] = item.Key.Index;
                    k++;
                }
                //输出同化后的最优估计
                PreModel.Parameter.ModelOutputParameter.IsOutInitial = true;

                PreModel.Parameter.ModelProcessParameter.ModelStartTime = Parameter.Pre_StartTime;
                PreModel.Parameter.SetStartTimeAsOutFile();// .ModelOutputParameter.OutDataBaseFileName = PreModel.Parameter.ModelProcessParameter.ModelStartTime.DateTimeTofileName(".nc");
                PreModel.Prepost();
                //如果不是热启动，进行预处理计算,生成初始值
                if (!Parameter.IsHotStartPrediction)
                {
                    Blog.BlogPromptInfo("========Prediction model start simulate to get initial predition value=============");
                    PreModel.Parameter.ModelProcessParameter.ModelStartTime = Parameter.Pre_StartTime - Parameter.Pre_predictionTime;
                    PreModel.Parameter.ModelProcessParameter.ModelSpanTime = Parameter.Pre_predictionTime;

                    //PreModel.Parameter.Prepost(PreModel);
                    bool rVal = true;
                    rVal = rVal && PreModel.Parameter.Initial(PreModel);
                    ModelTimeProcessParameter par = PreModel.Parameter.ModelProcessParameter;
                    Blog.BlogInfos(par.GetProcInfo(0));
                    par.StartRun();

                    for (par.CurrentStep = 0; rVal && par.CurrentStep < par.TotalSteps && rVal; par.CurrentStep++)
                    {
                        rVal = rVal && PreModel.StepSolution();
                    }
                    if (rVal)
                    {
                        Blog.BlogProcInfo($"The project has been complete. Data:{DateTime.Now}", 1.0);
                    }
                    else
                    {
                        Blog.BlogProcInfo("An error occurred during the calculation!", 1.0);
                    }
                    par.EndRun();
                    Blog.BlogProcInfo($"{par.GetEndRunInfo()}", 1.0);
                    Blog.BlogPromptInfo("========Prediction model have get initial predition value=============");
                }
                               
                                           

                NewAssembelModel();

                //
                for (int i = 0; i < Parameter.StateVariable.Count; i++)
                {
                    var item = Parameter.StateVariable[i];
                    if (item.VariableType == Enums.DAVariableType.Foreast)
                    {
                        if (!PreModel.Num.ContainsKey(item.Name.ToUpper()))
                        {
                            Blog.BlogErrorInfo($"Model not contain {item.Name} variable");
                        }
                    }
                    for (int j = item.StartIndex; j < item.StartIndex + item.Range; j++)
                        enkf_QMtx[j] = item.Error;
                }

                GetStateVariable(PreModel, ref enkf_aAVect, true);
                IntialStateVariable();

                return true;
            }
            catch (Exception)
            {

                throw;
            }
        }
        /// <summary>
        /// 由上一次预报至当前预报
        /// </summary>
        public bool DataAssimilation()
        {
            try
            {
                int nstep = (int)(Parameter.Pre_timeStep / Parameter.Obv_timeStep);
                for (int i = 0; i < N; i++) models[i].Parameter.ModelProcessParameter.ModelStartTime = Parameter.Pre_StartTime;
                for (int i = 0; i < nstep; i++)
                {
                    StepObserverDA();
                }

                return true;
            }
            catch (Exception)
            {
                throw;
            }

        }
        /// <summary>
        /// 预报
        /// </summary>
        /// <returns>是否成功预报计算</returns>
        public bool Predicion()
        {
            try
            {
                //开始预报
                
                SetStateVariable(PreModel, enkf_aAVect);
                PreModel.Parameter.ModelProcessParameter.ModelStartTime = Parameter.Pre_StartTime; //????设置为当前时间
                PreModel.Parameter.ModelProcessParameter.ModelSpanTime = Parameter.Pre_timeLong;

                //PreModel.Parameter.Prepost(PreModel);
                bool rVal = true;
                rVal = rVal && PreModel.Parameter.Initial(PreModel);
                rVal = rVal && InitialDatabase();
                ModelTimeProcessParameter par = PreModel.Parameter.ModelProcessParameter;

                par.StartRun();
                for (par.CurrentStep = 0; rVal && par.CurrentStep < par.TotalSteps && rVal; par.CurrentStep++)
                {
                    rVal = rVal && PreModel.StepSolution();
                    rVal = rVal && SaveProcData();
                    if (par.CurrentTime == par.ModelStartTime + Parameter.Obv_timeStep)
                    {
                        Intiialdata = PreModel.Parameter.ModelInitialParameter.Clone() as ModelInitialParameter;
                    }

                    if (par.CurrentTime == par.ModelStartTime +  3600)
                    {
                        GetStateVariable(PreModel, ref VariableSavepre, true);
                    }
                }
                if (rVal)
                {
                    Blog.BlogProcInfo($"The project has been complete. Data:{DateTime.Now}", 1.0);
                }
                else
                {
                    Blog.BlogProcInfo("An error occurred during the calculation!", 1.0);
                }
                par.EndRun();
                Blog.BlogProcInfo($"{par.GetEndRunInfo()}", 1.0);
                rVal = rVal && EndSaveData();
                PreModel.Parameter.ModelInitialParameter = (ModelInitialParameter)Intiialdata.Clone();
                return rVal;
            }
            catch (Exception)
            {

                throw;
            }

        }

        /// <summary>
        /// 由上一个观测至下一个观测的数据同化
        /// </summary>
        public void StepObserverDA()
        {
            try
            {
                //生成预测状态变量集合
                for (int i = 0; i < N; i++)
                {
                    int nstep = (int)(Parameter.Obv_timeStep / models[i].Parameter.ModelProcessParameter.TimeStep);
                    for (int j = 0; j < nstep; j++)
                    {
                        models[i].StepSolution();
                    }
                    GetStateVariable(models[i], ref enkf_aVect[i]);
                }
                //获得观测值
                GetObeserveValue(models[0].Parameter.ModelProcessParameter.CurrentTime);
                //获得调整糙率的观测值
                GetRoughadjObeserveValue(PreModel.Parameter.ModelProcessParameter.ModelStartTime + Parameter.Obv_timeStep);

                //同化生成最优估计变量集合
                ENKF();
                //最优变量集合复制给模型集合
                for (int i = 0; i < N; i++)
                {
                    SetStateVariable(models[i], enkf_aVect[i]);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }
        /// <summary>
        /// 集合卡尔曼滤波估算最优转态变量
        /// </summary>
        public void ENKF()
        {
            try
            {
                for (int i = 0; i < N; i++)
                {
                    double a = 0.05;
                    for (int j = 0; j < m; j++)
                    {
                        enkf_aVect[i][RoughnessIndex[j]] = enkf_aVect[i][RoughnessIndex[j]] * (1 + a * (Roughadj_obv[j] - VariableSavepre[enkf_HIndex[j]]) / Roughadj_obv[j]);
                    }
                }


                for (int j = 0; j < n; j++)
                {
                    double r = enkf_QMtx[j];
                    for (int i = 0; i < N; i++)
                    {
                        enkf_aVect[i][j] += GenerateRadom(r, i, j);
                    }
                }
                //hx
                double[][] hx = new double[N][];

                for (int i = 0; i < N; i++)
                {
                    hx[i] = new double[m];
                    for (int j = 0; j < m; j++)
                    {
                        hx[i][j] = enkf_aVect[i][enkf_HIndex[j]];
                    }
                }
                //average( hx)
                double[] hxa = new double[m];
                for (int j = 0; j < m; j++)
                {
                    for (int i = 0; i < N; i++)
                    {
                        hxa[j] += hx[i][j];
                    }
                    hxa[j] /= N;
                }
                //Hx - average(Hx)
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        hx[i][j] -= hxa[j];
                    }
                }
                //average(x)
                
                for (int j = 0; j < n; j++)
                {
                    enkf_aAVect[j] = 0;
                    for (int i = 0; i < N; i++)
                    {
                        enkf_aAVect[j] += enkf_aVect[i][j];
                    }
                    enkf_aAVect[j] /= N;
                }
                //x = xfi -averge(xfi)
                double[][] x = new double[N][];
                for (int i = 0; i < N; i++)
                {
                    x[i] = new double[n];
                    for (int j = 0; j < n; j++)
                    {
                        x[i][j] = enkf_aVect[i][j] - enkf_aAVect[j];
                    }
                }
                //PH' 协方差（状态变量-测量值）
                for (int j = 0; j < n; j++)
                {
                    for (int k = 0; k < m; k++)
                    {
                        enkf_PHMtx[j, k] = 0;
                    }
                }
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        for (int k = 0; k < m; k++)
                        {
                            enkf_PHMtx[j, k] += x[i][j] * hx[i][k];
                        }
                    }
                }
                for (int j = 0; j < n; j++)
                {
                    for (int k = 0; k < m; k++)
                    {
                        enkf_PHMtx[j, k] /= (N - 1);
                    }
                }
                //hph' 测量的方差
                for (int j = 0; j < m; j++)
                {
                    for (int k = 0; k < m; k++)
                    {
                        enkf_HPHMtx[j, k] = 0;
                    }
                }
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        for (int k = 0; k < m; k++)
                        {
                            enkf_HPHMtx[j, k] += hx[i][j] * hx[i][k];
                        }
                    }
                }
                //hph' 统计无片
                for (int j = 0; j < m; j++)
                {
                    for (int k = 0; k < m; k++)
                    {
                        enkf_HPHMtx[j, k] /= (N - 1);
                    }
                }
                //hph' = hph'-R
                for (int k = 0; k < m; k++)
                {
                    enkf_HPHMtx[k, k] += enkf_RMtx[k] * enkf_RMtx[k];
                }
                //invers hph' 计算矩阵的逆矩阵
                Matrix<double> matrix = Matrix<double>.Build.DenseOfArray(enkf_HPHMtx);
                Matrix<double> inverse = matrix.Inverse();
                //计算K 卡尔曼增益
                Matrix<double> phMatrix = Matrix<double>.Build.DenseOfArray(enkf_PHMtx);
                Matrix<double> kMatrix = phMatrix * inverse;

                Blog.BlogInfor(kMatrix.ToString(kMatrix.RowCount, kMatrix.ColumnCount, "f3"));

                for (int i = 0; i < N; i++)
                {
                    //计算y0 - Hx
                    var y0 = new DenseVector(m);
                    //double[,] y = new double[m,1];
                    for (int k = 0; k < m; k++)
                    {
                        y0[k] = enkf_obv[k] + MathTool.GenerateNormalRadom(0, enkf_RMtx[k]) - enkf_aVect[i][enkf_HIndex[k]];
                        //y0[k] = enkf_obv[k]  - enkf_aVect[i][enkf_HIndex[k]];
                    }
                    Vector<double> var = kMatrix * y0;
                    for (int j = 0; j < n; j++) enkf_aVect[i][j] += var[j];
                }
                //计算平均值
                for (int j = 0; j < n; j++)
                {
                    enkf_aAVect[j] = 0;
                    for (int i = 0; i < N; i++)
                    {
                        enkf_aAVect[j] += enkf_aVect[i][j];
                    }
                    enkf_aAVect[j] /= N;
                }
                DenseVector vect = DenseVector.OfArray(enkf_aAVect);
                //Blog.BlogInfor(vect.ToString(vect.Count, 16, "f5"));
                OutAnalysisAndObverveError();
            }
            catch (Exception)
            {

                throw;
            }
        }
        /// <summary>
        /// 返回一个随机数
        /// </summary>
        /// <param name="r">方差</param>
        /// <param name="i">i</param>
        /// <param name="j">j</param>
        /// <returns>方差为r的随机数</returns>
        private double GenerateRadom(double r,int i,int j)
        {
            double zero = 0;
            return zero.GenerateNormalRadom(r, (i + N) * (j + n) + DateTime.Now.Millisecond);
        }
        /// <summary>
        /// 产生随机的集合变量
        /// </summary>
        public void IntialStateVariable()
        {
            for (int j = 0; j < n; j++)
            {
                double mean = enkf_aAVect[j];
                double variance = enkf_QMtx[j];
                for (int i = 0; i < N; i++)
                {
                    enkf_aVect[i][j] = mean.GenerateNormalRadom(variance, (i + N) * (j + n) + DateTime.Now.Millisecond);
                }
            }
            for (int i = 0; i < N; i++)
            {
                SetStateVariable(models[i], enkf_aVect[i]);
            }
        }
        /// <summary>
        /// 预测模型集合
        /// </summary>
        /// <returns>是否成功</returns>
        public bool NewAssembelModel()
        {
            try
            {
                Blog.BlogPromptInfo("==========Initial assemble prediction models =================");
                bool rval = true;
                models = new IHydraulicModel[N];

                if (PreModel is ICloneable pm)
                {
                    for (int i = 0; i < N; i++)
                    {
                        models[i] = pm.Clone() as IHydraulicModel;
                        models[i].ProjectName = $"Assemble{i}";
                        rval = rval && models[i].Prepost();
                    }
                }
                //var cmdLines = PreModel.ToStrings().ToTxtFileStringArray();
                //Blog.BlogInfos(cmdLines, false, false);
                ////Parallel.For(0, N, PreModel.Parameter.ModelCommonParameter.ParallelOptions, (int i) =>
                //for (int i = 0; i < N; i++)
                //{
                //    models[i] = Activator.CreateInstance(PreModel.GetType()) as IHydraulicModel;
                //    models[i].ProjectName = $"Assemble{i}";
                //    models[i].InString(ref cmdLines);
                //    rval = rval && models[i].Prepost();
                //    Blog.BlogPromptInfo($"==========Initial assemble {models[i].ProjectName} =================");
                //}
                //);
                Blog.BlogPromptInfo("==========Assemble prediction models is over =================");
                return rval;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 计算每个状态变量在转态变量矢量中的起始索引和范围
        /// </summary>
        protected virtual void SetStateVariableIndex()
        {
            try
            {
                Parameter.StateVariable[0].StartIndex = 0;
                for (int i = 1; i < Parameter.StateVariable.Count; i++)
                {
                    Parameter.StateVariable[i].StartIndex = Parameter.StateVariable[i - 1].StartIndex + Parameter.StateVariable[i - 1].Range;
                }
            }
            catch (Exception)
            {

                throw;
            }

        }
        /// <summary>
        /// 获得转态变量的个数
        /// </summary>
        /// <returns>状态变量个数</returns>
        protected virtual int GetVariableNumber()
        {
            try
            {
                int rval = 0;
                for (int i = 0; i < Parameter.StateVariable.Count; i++)
                {
                    rval += Parameter.StateVariable[i].Range;

                }
                return rval;
            }
            catch (Exception)
            {

                throw;
            }

        }
        /// <summary>
        /// 获得状态变量的值
        /// </summary>
        /// <param name="model">模型</param>
        /// <param name="variable">状态变量</param>
        protected virtual void GetStateVariable(IHydraulicModel model, ref double[] variable, bool isFirst = false)
        {
            try
            {
                for (int i = 0; i < Parameter.StateVariable.Count; i++)
                {
                    var item = Parameter.StateVariable[i];
                    int index = item.StartIndex;
                    if (item.VariableType == Enums.DAVariableType.Foreast)
                    {
                        double[] tarray = model.Num[item.Name.ToUpper()] as double[];
                        for (int j = 0; j < item.Range; j++)
                        {
                            int k = index + j;
                            variable[k] = tarray[j];
                        }
                    }
                    else if (isFirst && item.VariableType == Enums.DAVariableType.Parameter)  //作为参数
                    {
                        double[] tarray = null;
                        if (model.Num.ContainsKey(item.Name.ToUpper())) tarray = model.Num[item.Name] as double[];
                        else if (item.Name == VariableName.H1_Roughness)
                        {
                            var rtopo = models[i].HyModelTopo.ToRiverNetworkTopo();
                            if (rtopo != null) tarray = rtopo.GetRoughness();
                        }
                        if (tarray != null)
                        {
                            for (int j = 0; j < item.Range; j++)
                            {
                                if (item is StateSpaceVariableInfo stateSpaceVariableInfo)
                                {
                                    int jj = stateSpaceVariableInfo.SpaceGroup[j][0];
                                    int k = index + j;
                                    variable[k] = tarray[jj];
                                }
                                else
                                {
                                    int k = index + j;
                                    variable[k] = tarray[j];
                                }
                            }
                        }
                    }
                    else if (isFirst && item.VariableType == Enums.DAVariableType.Boundary)
                    {
                        if (item is StateBoundaryVariableInfo sbvi)
                        {
                            if(model.GetSerial(sbvi.Name) is IParameterIndex par)
                            {
                                double tv = (double)par.GetParameter(sbvi.ParameterIndex);
                                int k = index;
                                variable[k] = tv;
                            }
                            
                        }
                    }
                }
                //for (int i = 0; i < n; i++)
                //{
                //    variable[i] += MathTool.GenerateNormalRadom(0, enkf_QMtx[i]);
                //}
            }
            catch (Exception)
            {

                throw;
            }
        }
        /// <summary>
        /// 设置状态变量值到模型中
        /// </summary>
        /// <param name="model">模型</param>
        /// <param name="variable">变量值</param>
        protected virtual void SetStateVariable(IHydraulicModel model, double[] variable)
        {
            try
            {
                for (int i = 0; i < Parameter.StateVariable.Count; i++)
                {
                    var item = Parameter.StateVariable[i];
                    if (!item.IsResetToModel) { continue; }
                    int index = item.StartIndex;
                    if (item.VariableType == Enums.DAVariableType.Foreast)
                    {
                        if (model is IParameterIndex par) //如果模型实现了接口，可以通过接口给模型传递同化状态变量
                        {
                            double[] tarray = new double[item.Range];
                            for (int j = 0; j < item.Range; j++)
                            {
                                int k = index + j;
                                tarray[j] = Math.Max(item.Min, Math.Min(item.Max, variable[k]));
                            }
                            par.SetParameter(item.ParameterIndex, tarray);
                        }
                        else //如果模型没有实现了接口，通过接口数据接口直接修改同化状态变量，该方法可能存在模型对输入状态变量进行重新计算或相关计算的问题
                        {
                            double[] tarray = model.Num[item.Name] as double[];
                            for (int j = 0; j < item.Range; j++)
                            {
                                int k = index + j;
                                tarray[j] = Math.Max(item.Min, Math.Min(item.Max, variable[k]));
                            }
                        }

                    }
                    else if (item.VariableType == Enums.DAVariableType.Parameter)
                    {
                        if (model is IParameterIndex par) //如果模型实现了接口，可以通过接口给模型传递同化状态变量
                        {
                            double[] tarray = new double[item.Range];
                            for (int j = 0; j < item.Range; j++)
                            {
                                int k = index + j;
                                tarray[j] = Math.Max(item.Min, Math.Min(item.Max, variable[k]));
                            }
                            if (item is StateSpaceVariableInfo ssvi)
                            {
                                par.SetParameter(item.ParameterIndex, ssvi.ToModelVariable(tarray));
                            }
                            else par.SetParameter(item.ParameterIndex, tarray);
                        }
                        else if (model.Num.ContainsKey(item.Name))
                        {
                            double[] tarray = model.Num[item.Name] as double[];
                            for (int j = 0; j < item.Range; j++)
                            {
                                if (item is StateSpaceVariableInfo stateSpaceVariableInfo)
                                {
                                    int k = index + j;

                                    for (int jj = 0; jj < stateSpaceVariableInfo.SpaceGroup[j].Count; jj++)
                                    {
                                        int tid = stateSpaceVariableInfo.SpaceGroup[j][jj];
                                        tarray[tid] = Math.Max(item.Min, Math.Min(item.Max, variable[k]));  //variable[k];
                                    }
                                }
                                else
                                {
                                    int k = index + j;
                                    tarray[j] = variable[k];
                                }
                            }
                        }
                        else if (item.Name == VariableName.H1_Roughness)
                        {
                            var rtopo = model.HyModelTopo.ToRiverNetworkTopo();
                            double[] tarray = new double[rtopo.Nn];
                            if (item is StateSpaceVariableInfo stateSpaceVariableInfo)
                            {
                                for (int j = 0; j < item.Range; j++)
                                {
                                    int k = index + j;

                                    for (int jj = 0; jj < stateSpaceVariableInfo.SpaceGroup[j].Count; jj++)
                                    {
                                        int tid = stateSpaceVariableInfo.SpaceGroup[j][jj];
                                        tarray[tid] = Math.Max(item.Min, Math.Min(item.Max, variable[k]));
                                    }
                                }
                            }
                            else
                            {
                                for (int j = 0; j < item.Range; j++)
                                {
                                    int k = index + j;
                                    tarray[j] = Math.Max(item.Min, Math.Min(item.Max, variable[k]));
                                }
                            }
                            rtopo.SetRoughness(tarray);
                        }
                    }
                    else if (item.VariableType == Enums.DAVariableType.Boundary)
                    {
                        if (item is StateBoundaryVariableInfo sbvi)
                        {
                            double[] tarray = new double[item.Range];
                            for (int j = 0; j < item.Range; j++)
                            {
                                int k = index + j;
                                tarray[j] = Math.Max(item.Min, Math.Min(item.Max, variable[k]));
                            }
                            if (model.GetSerial(sbvi.Name) is IParameterIndex par)
                                par.SetParameter(sbvi.ParameterIndex, tarray);
                        }
                    }
                }
            }
            catch (Exception)
            {

                throw;
            }
        }
        /// <summary>
        /// 生成变量控件
        /// </summary>
        /// <returns></returns>
        protected bool NewEnkfMatrix()
        {
            enkf_HIndex = new int[m];
            enkf_RMtx = new double[m];
            enkf_QMtx = new double[n];
            enkf_PHMtx = new double[n, m];
            enkf_HPHMtx = new double[m, m];
            enkf_HXMtx = new double[m];
            enkf_HXAMtx = new double[m];
            enkf_aAVect = new double[n];
            enkf_aVect = new double[N][];
            
            VariableSavepre = new double[n];
            RoughnessIndex = new int[m];
            Roughadj_obv= new double[m];
            for (int i = 0; i < N; i++)
            {
                enkf_aVect[i] = new double[n];
            }
            enkf_obv = new double[m];

            return false;
        }

        /// <summary>
        /// 获得指定时刻的观测向量
        /// </summary>
        /// <param name="time"></param>
        private void GetObeserveValue(double time)
        {
            for (int i = 0; i < m; i++)
            {
                enkf_obv[i] = obv_Series[i].GetSeriesValue(time);
            }
        }
        private void GetRoughadjObeserveValue(double time)
        {
            for (int i = 0; i < m; i++)
            {
                Roughadj_obv[i] = obv_Series[i].GetSeriesValue(time);
            }
        }
        #region dbOUt
        /// <summary>
        /// 初始化数据库
        /// </summary>
        /// <param name="model">模型</param>
        /// <returns>是否成功</returns>
        protected bool InitialDatabase(IHydraulicModel model)
        {
            ModelOutputParameter par = model.Parameter.ModelOutputParameter;
            IModelDataBase db = GlobalVariable.GetInstance(par.GetOutDatabaseType()) as IModelDataBase;
            model.ProjectName ??= GlobalVariable.AutoGenerateGlobalKey();
            string prjName = model.ProjectName;
            Db[prjName] = db;
            if (db == null) return false;

            model.Parameter.SetStartTimeAsOutFile();
            db.DataBaseFileName = model.Parameter.ModelOutputParameter.OutDataBaseFileName;
            db.Blog = model.Blog;
            var info = new DataBaseInfo
            {
                Topo = model.HyModelTopo
            };

            info.VariableInfo.Add(VariableName.M_Time, par.CanOutVariable[VariableName.M_Time]);
            foreach (var item in par.OutVariable)
            {
                info.Dimesionals = par.Dimesionals;
                if (par.CanOutVariable.ContainsKey(item)) { if (par.CanOutVariable[item].Dimensional.Count > 0) info.VariableInfo.Add(item, par.CanOutVariable[item]); }
                else Blog.BlogErrorInfo($"Model {model.ModelName} can not out put variable {item}");
            }
            db.DataBaseInfo = info;
            //设置非时间序列输出数据
            if (model is not IHydraulicModelNum num) return false;
            Dictionary<string, object> timeData = new();
            Dictionary<string, object> noTimeData = new();
            foreach (var item in info.VariableInfo)
            {
                if (item.Key == VariableName.M_Time)
                {
                    timeData.Add(item.Key, model.Parameter.ModelProcessParameter.Time);
                }
                else
                {
                    if (!num.Num.ContainsKey(item.Key)) { Blog.BlogErrorInfo($"This model can not out variable {item.Key}"); return false; }
                    if (par.Dimesionals[item.Value.Dimensional[0]].Length == -1)//时间序列
                    {
                        timeData.Add(item.Key, num.Num[item.Key]);
                    }
                    else  //非时间过程
                    {
                        noTimeData.Add(item.Key, num.Num[item.Key]);
                    }
                }
            }
            OutTimeData[prjName] = timeData;
            OutNoTimeData[prjName] = noTimeData;

            bool rval = db.SaveProcHeaderInfor();
            if (rval)
            {
                db.SaveProcStepInfor(noTimeData);
                if (par.IsOutInitial)
                {
                    string infos = model.Parameter.ModelProcessParameter.GetProcessInfoString(out double rat);
                    Blog.BlogProcInfo(infos, rat);
                    db.SaveProcStepInfor(timeData);
                }
            }
            return rval;
        }
        /// <summary>
        /// 初始化模型结果数据库
        /// </summary>
        /// <returns>是否成功</returns>
        public bool InitialDatabase()
        {
            bool rval = true;
            try
            {
                if (PreModel is IHydraulicCoupling1D2DModel h12d)
                {
                    rval = rval && InitialDatabase(h12d.Hy1D);
                    rval = rval && InitialDatabase(h12d.Hy2D);
                }
                else
                {
                    rval = rval && InitialDatabase(PreModel);
                }
                return rval;
            }
            catch (Exception ex)
            {
                Blog.BlogErrorInfo(new PHMException.PHMHyModelException(PreModel, ex, "InitialDatabase"));
                return false;
            }

        }

        /// <summary>
        /// 保存数据
        /// </summary>
        /// <returns>是否成功</returns>
        public bool SaveProcData()
        {
            ModelTimeProcessParameter par = PreModel.Parameter.ModelProcessParameter;
            if (par.IsSave()) //模型是否需要输出数据
            {
                bool rval = true;
                try
                {
                    PreModel.CaculateForOut();
                    string info = par.GetProcessInfoString(out double rat);
                    Blog.BlogProcInfo(info, rat);
                    foreach (var item in Db)
                    {
                        rval = rval && item.Value.SaveProcStepInfor(OutTimeData[item.Key]);
                    }
                    return rval;
                }
                catch (Exception ex)
                {
                    Blog.BlogErrorInfo(new PHMException.PHMHyModelException(PreModel, ex, "SaveProcData"));
                    return false;
                }
            }
            else return true;
        }
        /// <summary>
        /// 关闭输出数据库
        /// </summary>
        /// <returns>结束模型输出去</returns>
        public bool EndSaveData()
        {
            bool rval = true;
            try
            {
                foreach (var item in Db)
                {
                    rval = rval && item.Value.EndSaveProc();
                }
                return rval;
            }
            catch (Exception ex)
            {
                Blog.BlogErrorInfo(new PHMException.PHMHyModelException(PreModel, ex, "SaveProcData"));
                return false;
            }
        }
        #endregion

        /// <summary>
        /// 输出观测值与估计值之间的偏差
        /// </summary>
        protected void OutAnalysisAndObverveError()
        {
            List<string> lines = new();
            for (int k = 0; k < m; k++)
            {
                double analysis = enkf_aAVect[enkf_HIndex[k]];
                lines.Add($"{enkf_obv[k]}\t{analysis}\t{enkf_obv[k] - analysis}");
            }
            Blog.BlogInfor("Analysis and Obverve Error");
            Blog.BlogInfos(lines);
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
                    Parameter = rd.ReadObject() as EnkfParameter;
                    PreModel = rd.ReadObject() as IHydraulicModel;
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
                wr.WriteObject(Parameter);
                wr.WriteObject(PreModel);
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

                List<TxtFileString> strings = new();
                for (; i < lines.Count; i++)
                {
                    TxtFileString line = lines[i];
                    List<string> strs = line.SubStrings();
                    if (strs.Count <= 0) continue;
                    cmd = strs[0];
                    if (cmd.IsCommond("Version")) { _ = byte.TryParse(strs[1], out tv); }
                    else if (cmd.IsCommond(new string[] { "Parameter" }))
                    {
                        List<TxtFileString> tline = lines.GetCommondStrings(cmd, ref i, i + 1);
                        Parameter.InString(ref tline, par);
                    }
                    else if (cmd.IsCommond(new string[] { "Model", "SOLVER" }))
                    {
                        List<TxtFileString> tline = lines.GetCommondStrings(cmd, ref i, i + 1);
                        PreModel = GlobalVariable.GetInstance(strs[1]) as IHydraulicModel;
                        PreModel.InString(ref tline, par);
                    }
                    else strings.Add(line);
                }
                base.InString(ref strings, par);
                lines = strings;
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

                rval.AddRange(Parameter.ToStrings(level + 1, par, "Parameter"));
                rval.AddRange(PreModel.ToStrings(level + 1, par, "SOLVER"));

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
