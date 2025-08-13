using Common.Enums;
using Common.ModelParameter;

namespace Common.Model.DA
{
    /// <summary>
    /// 同化数据的输出控制
    /// </summary>
    public class EnkfOutParameter: ModelParameterBase
    {
        /// <summary>
        /// 输出的步数，每同化多少步输出一次数据
        /// </summary>
        public int OutStep { get; set; } = 1;
        /// <summary>
        /// 数据输出格式
        /// </summary>
        public OutDataBaseType OutDataBaseType { get; set; } = OutDataBaseType.Auto;
        /// <summary>
        /// 输出数据名称
        /// </summary>
        public string OutDataBaseFileName { get; set; } = string.Empty;
        /// <summary>
        /// 输出文件目录
        /// </summary>
        public string OutDicionary { get; set; } = string.Empty;
        /// <summary>
        /// 输出变量
        /// </summary>
        public List<string> OutVariable { get; set; } = new();
    }
}
