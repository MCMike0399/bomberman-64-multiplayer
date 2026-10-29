extends SceneTree

func _initialize():
 var args = OS.get_cmdline_user_args()
 var mode = args[0] if not args.is_empty() else "low-pending"
 var drainer = load("res://ProbeDrain.cs").new()
 var jobs = []
 for index in range(32):
  jobs.append(WorkerThreadPool.add_task(Callable(OS, "delay_msec").bind(10), false, "BlastExitDelay"))
 if mode == "low-fenced":
  var pending = 0
  for job in jobs:
   if not WorkerThreadPool.is_task_completed(job):
    pending += 1
  print("BLAST_PENDING_BEFORE_DRAIN ", pending)
  if pending == 0:
   printerr("ERROR: no pending work exercised the shutdown drain")
   quit(1)
   return
  if not drainer.Drain():
   printerr("ERROR: shutdown drain failed")
   quit(1)
   return
  for job in jobs:
   if not WorkerThreadPool.is_task_completed(job):
    printerr("ERROR: prior work remained after shutdown drain")
    quit(1)
    return
   WorkerThreadPool.wait_for_task_completion(job)
  print("BLAST_PRIOR_TASKS_COMPLETE ", jobs.size())
 print("BLAST_POOL_PROBE quit mode=", mode, " jobs=", jobs.size())
 quit(0)
