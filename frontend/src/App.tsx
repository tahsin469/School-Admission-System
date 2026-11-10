import React, { useState, useEffect } from 'react';
import { 
  fetchClasses, 
  holdSeat, 
  confirmSeat, 
  cancelHold, 
  createClass,
  SeatStatus,
  type ClassItem, 
  type Seat
} from './api/client';
import { 
  GraduationCap, 
  Clock, 
  CheckCircle2, 
  AlertCircle, 
  RefreshCw, 
  UserCheck, 
  PlusCircle, 
  X
} from 'lucide-react';

export default function App() {
  const [classes, setClasses] = useState<ClassItem[]>([]);
  const [selectedClass, setSelectedClass] = useState<ClassItem | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
  const [activeTab, setActiveTab] = useState<'admission' | 'admin'>('admission');

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [targetSeat, setTargetSeat] = useState<Seat | null>(null);
  const [studentId, setStudentId] = useState('');
  const [modalAction, setModalAction] = useState<'hold' | 'confirm' | 'cancel'>('hold');
  const [errorMessage, setErrorMessage] = useState('');
  const [successMessage, setSuccessMessage] = useState('');
  const [actionLoading, setActionLoading] = useState(false);

  // Admin Form State
  const [newClassName, setNewClassName] = useState('');
  const [newTotalSeats, setNewTotalSeats] = useState(30);
  const [newAdmissionFee, setNewAdmissionFee] = useState(2000);

  // 1. Short Polling: Fetch classes every 3 seconds as required
  const loadClasses = async () => {
    try {
      const data = await fetchClasses();
      setClasses(data);
      setLastUpdated(new Date());

      // Update selected class if active
      if (selectedClass) {
        const updated = data.find(c => c.id === selectedClass.id);
        if (updated) setSelectedClass(updated);
      } else if (data.length > 0) {
        setSelectedClass(data[0]);
      }
    } catch (err) {
      console.error('Polling error:', err);
    }
  };

  useEffect(() => {
    loadClasses();
    const interval = setInterval(() => {
      loadClasses();
    }, 3000); // 3-second polling interval

    return () => clearInterval(interval);
  }, []);

  // 2. Action Handlers
  const handleSeatClick = (seat: Seat) => {
    setErrorMessage('');
    setSuccessMessage('');
    setTargetSeat(seat);

    if (seat.status === SeatStatus.Available) {
      setModalAction('hold');
      setStudentId('');
      setIsModalOpen(true);
    } else if (seat.status === SeatStatus.Held) {
      setModalAction('confirm');
      setStudentId(seat.currentStudentId || '');
      setIsModalOpen(true);
    }
  };

  const executeSeatAction = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedClass) return;

    if (!/^[0-9]{17}$/.test(studentId)) {
      setErrorMessage('Student ID must be an exact 17-digit Birth Registration Number.');
      return;
    }

    setActionLoading(true);
    setErrorMessage('');
    setSuccessMessage('');

    try {
      if (modalAction === 'hold') {
        await holdSeat(selectedClass.id, studentId);
        setSuccessMessage('Seat held successfully for 120 seconds!');
      } else if (modalAction === 'confirm') {
        await confirmSeat(selectedClass.id, studentId);
        setSuccessMessage('Seat confirmed successfully!');
      } else if (modalAction === 'cancel') {
        await cancelHold(selectedClass.id, studentId);
        setSuccessMessage('Seat hold cancelled and released.');
      }

      await loadClasses();
      setTimeout(() => {
        setIsModalOpen(false);
      }, 1000);
    } catch (err: any) {
      setErrorMessage(err.message || 'An error occurred during operation.');
    } finally {
      setActionLoading(false);
    }
  };

  const handleReleaseHoldDirectly = async () => {
    if (!selectedClass || !targetSeat) return;
    setActionLoading(true);
    setErrorMessage('');
    setSuccessMessage('');

    try {
      await cancelHold(selectedClass.id, studentId);
      setSuccessMessage('Seat hold successfully released and opened for others!');
      await loadClasses();
      setTimeout(() => {
        setIsModalOpen(false);
      }, 700);
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to release seat hold.');
    } finally {
      setActionLoading(false);
    }
  };

  const handleCreateClass = async (e: React.FormEvent) => {
    e.preventDefault();
    setActionLoading(true);
    setErrorMessage('');
    setSuccessMessage('');

    try {
      await createClass(newClassName, newTotalSeats, newAdmissionFee);
      setSuccessMessage(`Class "${newClassName}" created successfully!`);
      setNewClassName('');
      await loadClasses();
      setActiveTab('admission');
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to create class.');
    } finally {
      setActionLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 p-4 md:p-8">
      {/* Header */}
      <header className="max-w-6xl mx-auto mb-8 flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-slate-800 pb-6">
        <div className="flex items-center gap-3">
          <div className="p-3 bg-blue-600 rounded-xl text-white shadow-lg shadow-blue-500/20">
            <GraduationCap className="w-8 h-8" />
          </div>
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-white">School Admission System</h1>
            <p className="text-sm text-slate-400">Real-Time Seat Allocation with PostgreSQL Row-Level Locking</p>
          </div>
        </div>

        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2 text-xs text-slate-400 bg-slate-900 border border-slate-800 px-3 py-1.5 rounded-full">
            <RefreshCw className="w-3.5 h-3.5 text-blue-400 animate-spin" />
            <span>Short Polling (3s): {lastUpdated.toLocaleTimeString()}</span>
          </div>

          <div className="flex bg-slate-900 border border-slate-800 rounded-lg p-1">
            <button
              onClick={() => setActiveTab('admission')}
              className={`px-3 py-1.5 rounded-md text-sm font-medium transition ${
                activeTab === 'admission' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white'
              }`}
            >
              Admissions
            </button>
            <button
              onClick={() => setActiveTab('admin')}
              className={`px-3 py-1.5 rounded-md text-sm font-medium transition ${
                activeTab === 'admin' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white'
              }`}
            >
              Admin
            </button>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="max-w-6xl mx-auto">
        {activeTab === 'admission' ? (
          <div>
            {/* Class Tabs */}
            <div className="flex gap-2 mb-6 overflow-x-auto pb-2">
              {classes.map((c) => (
                <button
                  key={c.id}
                  onClick={() => setSelectedClass(c)}
                  className={`px-5 py-3 rounded-xl border text-left transition flex-shrink-0 min-w-[200px] ${
                    selectedClass?.id === c.id
                      ? 'bg-blue-600/10 border-blue-500 text-white'
                      : 'bg-slate-900 border-slate-800 text-slate-400 hover:border-slate-700'
                  }`}
                >
                  <div className="font-semibold text-base">{c.name}</div>
                  <div className="text-xs text-slate-400 mt-1">
                    Available: <span className="text-emerald-400 font-bold">{c.availableSeats}</span> / {c.totalSeats}
                  </div>
                </button>
              ))}
            </div>

            {/* Selected Class Seat Matrix */}
            {selectedClass && (
              <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl">
                {/* Status Bar */}
                <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-6 border-b border-slate-800 pb-4">
                  <div>
                    <h2 className="text-xl font-bold text-white">{selectedClass.name} - Seat Matrix</h2>
                    <p className="text-xs text-slate-400 mt-0.5">
                      Admission Fee: <span className="text-blue-400 font-bold">BDT {selectedClass.admissionFee}</span>
                    </p>
                  </div>

                  {/* Legend */}
                  <div className="flex flex-wrap items-center gap-4 text-xs">
                    <div className="flex items-center gap-1.5">
                      <span className="w-3.5 h-3.5 rounded bg-emerald-500/20 border border-emerald-500"></span>
                      <span className="text-slate-300">Available ({selectedClass.availableSeats})</span>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <span className="w-3.5 h-3.5 rounded bg-amber-500/20 border border-amber-500"></span>
                      <span className="text-slate-300">Held 120s ({selectedClass.heldSeats})</span>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <span className="w-3.5 h-3.5 rounded bg-rose-500/20 border border-rose-500"></span>
                      <span className="text-slate-300">Confirmed ({selectedClass.confirmedSeats})</span>
                    </div>
                  </div>
                </div>

                {/* Seat Grid */}
                <div className="grid grid-cols-4 sm:grid-cols-6 md:grid-cols-10 gap-3">
                  {selectedClass.seats.map((seat) => {
                    const isAvail = seat.status === SeatStatus.Available;
                    const isHeld = seat.status === SeatStatus.Held;
                    const isConf = seat.status === SeatStatus.Confirmed;

                    return (
                      <button
                        key={seat.id}
                        disabled={isConf}
                        onClick={() => handleSeatClick(seat)}
                        className={`aspect-square rounded-xl p-2 flex flex-col items-center justify-between border transition-all transform hover:scale-105 ${
                          isAvail
                            ? 'bg-emerald-950/40 border-emerald-500/40 hover:border-emerald-400 text-emerald-300 cursor-pointer shadow-emerald-900/20 shadow-md'
                            : isHeld
                            ? 'bg-amber-950/40 border-amber-500/50 hover:border-amber-400 text-amber-300 cursor-pointer shadow-amber-900/20 shadow-md'
                            : 'bg-rose-950/30 border-rose-900/50 text-rose-500/70 cursor-not-allowed'
                        }`}
                      >
                        <span className="text-xs font-semibold">#{seat.seatNumber}</span>
                        {isAvail && <span className="text-[10px] uppercase font-bold text-emerald-400">Open</span>}
                        {isHeld && (
                          <div className="flex items-center gap-0.5 text-[10px] font-bold text-amber-400">
                            <Clock className="w-2.5 h-2.5" />
                            <span>{seat.remainingSeconds}s</span>
                          </div>
                        )}
                        {isConf && <CheckCircle2 className="w-4 h-4 text-rose-500" />}
                      </button>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        ) : (
          /* Admin Tab */
          <div className="max-w-xl mx-auto bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl">
            <div className="flex items-center gap-2 mb-4 text-white font-bold text-lg">
              <PlusCircle className="w-5 h-5 text-blue-500" />
              <span>Create New Class</span>
            </div>

            {errorMessage && (
              <div className="mb-4 p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg text-rose-400 text-sm flex items-center gap-2">
                <AlertCircle className="w-4 h-4" />
                <span>{errorMessage}</span>
              </div>
            )}

            {successMessage && (
              <div className="mb-4 p-3 bg-emerald-500/10 border border-emerald-500/30 rounded-lg text-emerald-400 text-sm flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4" />
                <span>{successMessage}</span>
              </div>
            )}

            <form onSubmit={handleCreateClass} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-slate-400 mb-1">Class Name</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Class 4"
                  value={newClassName}
                  onChange={(e) => setNewClassName(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-400 mb-1">Total Seats (Capacity)</label>
                <input
                  type="number"
                  required
                  min="1"
                  max="500"
                  value={newTotalSeats}
                  onChange={(e) => setNewTotalSeats(parseInt(e.target.value) || 0)}
                  className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-400 mb-1">Admission Fee (BDT)</label>
                <input
                  type="number"
                  required
                  min="0"
                  value={newAdmissionFee}
                  onChange={(e) => setNewAdmissionFee(parseFloat(e.target.value) || 0)}
                  className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <button
                type="submit"
                disabled={actionLoading}
                className="w-full bg-blue-600 hover:bg-blue-500 text-white font-medium py-2.5 rounded-lg text-sm transition shadow-lg shadow-blue-600/30"
              >
                {actionLoading ? 'Creating Class...' : 'Save & Initialize Seats'}
              </button>
            </form>
          </div>
        )}
      </main>

      {/* Seat Booking Modal */}
      {isModalOpen && targetSeat && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl max-w-md w-full p-6 shadow-2xl relative">
            <button
              onClick={() => setIsModalOpen(false)}
              className="absolute top-4 right-4 text-slate-400 hover:text-white"
            >
              <X className="w-5 h-5" />
            </button>

            <div className="flex items-center gap-2 mb-4">
              <div className="p-2 bg-blue-600/20 text-blue-400 rounded-lg">
                <UserCheck className="w-5 h-5" />
              </div>
              <div>
                <h3 className="font-bold text-white text-base">
                  {modalAction === 'hold' ? 'Hold Seat' : 'Confirm Seat'} #{targetSeat.seatNumber}
                </h3>
                <p className="text-xs text-slate-400">{selectedClass?.name}</p>
              </div>
            </div>

            {errorMessage && (
              <div className="mb-4 p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg text-rose-400 text-xs flex items-center gap-2">
                <AlertCircle className="w-4 h-4 flex-shrink-0" />
                <span>{errorMessage}</span>
              </div>
            )}

            {successMessage && (
              <div className="mb-4 p-3 bg-emerald-500/10 border border-emerald-500/30 rounded-lg text-emerald-400 text-xs flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 flex-shrink-0" />
                <span>{successMessage}</span>
              </div>
            )}

            <form onSubmit={executeSeatAction} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-slate-400 mb-1">
                  Birth Registration Number (17 digits)
                </label>
                <input
                  type="text"
                  required
                  maxLength={17}
                  placeholder="e.g. 20051234567890123"
                  value={studentId}
                  onChange={(e) => setStudentId(e.target.value.replace(/\D/g, ''))}
                  className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500 tracking-wider font-mono"
                />
                <p className="text-[11px] text-slate-500 mt-1">
                  Digits entered: {studentId.length} / 17
                </p>
              </div>

              {modalAction === 'hold' ? (
                <button
                  type="submit"
                  disabled={actionLoading || studentId.length !== 17}
                  className="w-full bg-emerald-600 hover:bg-emerald-500 disabled:bg-slate-800 disabled:text-slate-600 text-white font-medium py-2 rounded-lg text-sm transition"
                >
                  {actionLoading ? 'Holding Seat...' : 'Hold Seat (120s Countdown)'}
                </button>
              ) : (
                <div className="flex gap-2">
                  <button
                    type="submit"
                    disabled={actionLoading}
                    className="flex-1 bg-blue-600 hover:bg-blue-500 text-white font-medium py-2 rounded-lg text-sm transition"
                  >
                    {actionLoading ? 'Confirming...' : 'Confirm Admission'}
                  </button>
                  <button
                    type="button"
                    disabled={actionLoading}
                    onClick={handleReleaseHoldDirectly}
                    className="px-4 bg-slate-800 hover:bg-rose-950 text-slate-300 hover:text-rose-400 font-medium rounded-lg text-xs transition border border-slate-700 hover:border-rose-800"
                  >
                    {actionLoading ? 'Releasing...' : 'Release Hold'}
                  </button>
                </div>
              )}
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
