using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DtronixMessageQueue.ThreadPool {
	public class MqThreadPoolThread {
		private IMqThreadPoolWorkGroup owner;

		public MqThreadPoolThread(IMqThreadPoolWorkGroup owner) {
			this.owner = owner;
		}

	}
}
