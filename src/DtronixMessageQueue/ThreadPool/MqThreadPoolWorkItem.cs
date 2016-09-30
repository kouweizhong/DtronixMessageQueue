using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DtronixMessageQueue.ThreadPool {
	public class MqThreadPoolWorkItem {
		public Action Work { get; set; }
		public IMqThreadPoolWorkGroup WorkGroup { get; set; }
	}
}
