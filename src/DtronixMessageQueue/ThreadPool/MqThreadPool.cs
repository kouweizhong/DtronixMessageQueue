using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DtronixMessageQueue.ThreadPool {
	public class MqThreadPool : IMqThreadPoolWorkGroup {
		public MqThreadPoolConfig Config { get; set; }
		public int Concurrency { get; set; }
		public int InUseThreads { get; }
		public bool IsIdle { get; }

		private BlockingCollection<MqThreadPoolWorkItem> work_queue = new BlockingCollection<MqThreadPoolWorkItem>();

		private MqThreadPool base_pool;



		private MqThreadPool(MqThreadPool base_pool, MqThreadPoolConfig config) : this(config) {
			this.base_pool = base_pool;
		}

		public MqThreadPool(MqThreadPoolConfig config) {
			Config = config;
		}

		public IMqThreadPoolWorkGroup CreateWorkGroup(MqThreadPoolConfig config) {
			return new MqThreadPool(this, config);
		}

		public void QueueWorkItem(Action work) {
			var work_item = new MqThreadPoolWorkItem {
				Work = work
			};


			QueueWorkItem(work_item);
		}


		public void QueueWorkItem(MqThreadPoolWorkItem work) {
			if (base_pool != null) {
				base_pool.QueueWorkItem(new MqThreadPoolWorkItem() {
					WorkGroup = this
				});
			} else {
				if (work_queue.TryAdd(work) == false) {
					throw new InvalidOperationException("Work queue full.");
				}
			}
			
		}
	}
}
