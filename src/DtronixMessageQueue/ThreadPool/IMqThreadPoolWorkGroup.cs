using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DtronixMessageQueue.ThreadPool {
	public interface IMqThreadPoolWorkGroup {
		int Concurrency { get; set; }
		int InUseThreads { get; }
		bool IsIdle { get; }

		void QueueWorkItem(Action work_item);


	}
}
