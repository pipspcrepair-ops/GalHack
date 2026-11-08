extends Node
## A wall-clock limit on a test run, in GDScript so it stands even when a C# class fails to load:
## past `seconds` of real time it prints a failure and quits with exit code 1. Its own thread
## watches the clock, so a test stuck in a loop on the main thread still ends.

@export var seconds := 900.0

var _start := 0
var _thread: Thread
var _done := false


func _ready() -> void:
	_start = Time.get_ticks_msec()
	_thread = Thread.new()
	_thread.start(_watch)


func _process(_delta: float) -> void:
	if Time.get_ticks_msec() - _start > seconds * 1000.0:
		_quit()


func _watch() -> void:
	while not _done:
		OS.delay_msec(1000)
		if Time.get_ticks_msec() - _start > (seconds + 30.0) * 1000.0:
			printerr("FAIL  watchdog: the main thread is stuck past %d s" % int(seconds))
			OS.kill(OS.get_process_id())
			return


func _quit() -> void:
	if _done:
		return
	_done = true
	printerr("FAIL  watchdog: the run took over %d s" % int(seconds))
	get_tree().quit(1)


func _exit_tree() -> void:
	_done = true
	if _thread != null and _thread.is_started():
		_thread.wait_to_finish()
