﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

//using NLog;

namespace DtronixMessageQueue {
	public class MqPostmaster : IDisposable {
		public int MaxFrameSize { get; }
		private readonly MqWorker supervisor;

		private readonly ConcurrentDictionary<MqMailbox, bool> ongoing_write_operations = new ConcurrentDictionary<MqMailbox, bool>();
		private readonly BlockingCollection<MqMailbox> write_operations = new BlockingCollection<MqMailbox>();
		private readonly ConcurrentBag<MqWorker> write_workers = new ConcurrentBag<MqWorker>();


		private readonly ConcurrentDictionary<MqMailbox, bool> ongoing_read_operations = new ConcurrentDictionary<MqMailbox, bool>();
		private readonly BlockingCollection<MqMailbox> read_operations = new BlockingCollection<MqMailbox>();
		private readonly ConcurrentBag<MqWorker> read_workers = new ConcurrentBag<MqWorker>();

		public MqPostmaster(int max_frame_size) {
			MaxFrameSize = max_frame_size;
			// Add a supervisor to review when it is needed to increase or decrease the worker numbers.
			//supervisor = new MqWorker(SuperviseWorkers);

			// Create one reader and one writer workers to start off with.
			CreateReadWorker();
			CreateWriteWorker();

			//CreateWriteWorker();
		}


		public bool SignalWrite(MqMailbox mailbox) {
			return ongoing_write_operations.TryAdd(mailbox, true) && write_operations.TryAdd(mailbox);
		}

		private bool TryTakeWrite(out MqMailbox mailbox, int ms_timeout, CancellationToken token) {
			return write_operations.TryTake(out mailbox, ms_timeout, token);
		}

		private bool ReleaseWrite(MqMailbox mailbox) {
			bool out_mailbox;
			return ongoing_write_operations.TryRemove(mailbox, out out_mailbox);
		}


		public bool SignalRead(MqMailbox mailbox) {
			var result = ongoing_read_operations.TryAdd(mailbox, true);
			if (!result) {
				return false;
			}
			return read_operations.TryAdd(mailbox);
		}

		private bool TryTakeRead(out MqMailbox mailbox, int ms_timeout, CancellationToken token) {
			return read_operations.TryTake(out mailbox, ms_timeout, token);
		}

		private bool ReleaseRead(MqMailbox mailbox) {
			bool out_mailbox;
			return ongoing_read_operations.TryRemove(mailbox, out out_mailbox);
		}


		private async void StartSupervisor() {
			await Task.Delay(2000);
			supervisor.Start();
		}

		private async void SuperviseWorkers(CancellationToken token) {
			while (token.IsCancellationRequested == false) {
				if (read_workers.IsEmpty == false) {
					var read_averages = read_workers.Sum(worker => worker.AverageIdleTime)/read_workers.Count;

					if (read_averages < 50) {
						CreateReadWorker();
					}
				}

				if (write_workers.IsEmpty == false) {
					var write_averages = write_workers.Sum(worker => worker.AverageIdleTime)/write_workers.Count;

					if (write_averages < 50) {
						CreateWriteWorker();
					}
				}

				await Task.Delay(2000, token);
			}
		}


		/// <summary>
		/// Creates a worker writer.
		/// </summary>
		public void CreateWriteWorker() {
			var writer_worker = new MqWorker(token => {
				MqMailbox mailbox = null;

				try {
					while (TryTakeWrite(out mailbox, 60000, token)) {
						mailbox.ProcessOutbox();
						ReleaseWrite(mailbox);
					}
				} catch (ThreadAbortException) {
				} catch (Exception) {
					if (mailbox != null) {
						/*logger.Error(e,
							is_writer
								? "MqConnection {0}: Exception occurred while when writing."
								: "MqConnection {0}: Exception occurred while when reading.", mailbox.Connection.Id);*/
					}
				}
			});

			writer_worker.Start();

			write_workers.Add(writer_worker);
		}

		/// <summary>
		/// Creates a worker reader.
		/// </summary>
		public void CreateReadWorker() {
			var reader_worker = new MqWorker(token => {
				MqMailbox mailbox = null;

				try {
					while (TryTakeRead(out mailbox, 60000, token)) {
						mailbox.ProcessIncomingQueue();
						ReleaseRead(mailbox);
					}
				} catch (ThreadAbortException) {
				} catch (Exception) {
					if (mailbox != null) {
						/*logger.Error(e,
							is_writer
								? "MqConnection {0}: Exception occurred while when writing."
								: "MqConnection {0}: Exception occurred while when reading.", mailbox.Connection.Id);*/
					}
				}
			});

			reader_worker.Start();

			read_workers.Add(reader_worker);
		}

		public void Dispose() {
			supervisor.Stop();
			foreach (var write_worker in write_workers) {
				write_worker.Stop();
			}

			foreach (var read_worker in read_workers) {
				read_worker.Stop();
			}
		}
	}
}