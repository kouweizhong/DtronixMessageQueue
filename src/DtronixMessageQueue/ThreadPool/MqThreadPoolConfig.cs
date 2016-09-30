using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DtronixMessageQueue.ThreadPool {
	public class MqThreadPoolConfig {


		public ApartmentState ApartmentState { get; set; } = ApartmentState.STA;
		public bool AreThreadsBackground { get; set; } = true;
		public int IdleTimeout { get; set; } = 60000;
		public int? MaxStackSize { get; set; }
		public int MaxWorkerThreads { get; set; } = 5;
		public int MinWorkerThreads { get; set; } = 1;
		public string ThreadPoolName { get; set; } = "mq-thread-pool";
		public ThreadPriority ThreadPriority { get; set; } = ThreadPriority.Normal;


		public MqThreadPoolConfig(MqThreadPoolConfig config) {
			ApartmentState = config.ApartmentState;
			AreThreadsBackground = config.AreThreadsBackground;
			IdleTimeout = config.IdleTimeout;
			MaxStackSize = config.MaxStackSize;
			MaxWorkerThreads = config.MaxWorkerThreads;
			MinWorkerThreads = config.MinWorkerThreads;
			ThreadPoolName = config.ThreadPoolName;
			ThreadPriority = config.ThreadPriority;
		}

		public MqThreadPoolConfig() {
		}
	}
}
