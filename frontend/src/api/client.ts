export const SeatStatus = {
  Available: 0,
  Held: 1,
  Confirmed: 2
} as const;

export type SeatStatus = typeof SeatStatus[keyof typeof SeatStatus];

export interface Seat {
  id: string;
  seatNumber: number;
  status: SeatStatus;
  currentStudentId: string | null;
  heldAtUtc: string | null;
  expiresAtUtc: string | null;
  remainingSeconds: number;
}

export interface ClassItem {
  id: string;
  name: string;
  totalSeats: number;
  availableSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  admissionFee: number;
  seats: Seat[];
}

export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T;
  errors: string[];
  timestampUtc: string;
}

const API_BASE = 'http://localhost:8080';

export async function fetchClasses(): Promise<ClassItem[]> {
  const res = await fetch(`${API_BASE}/api/classes`);
  if (!res.ok) throw new Error('Failed to fetch classes');
  const json: ApiResponse<ClassItem[]> = await res.json();
  return json.data;
}

export async function fetchClassById(id: string): Promise<ClassItem> {
  const res = await fetch(`${API_BASE}/api/classes/${id}`);
  if (!res.ok) throw new Error('Failed to fetch class');
  const json: ApiResponse<ClassItem> = await res.json();
  return json.data;
}

export async function holdSeat(classId: string, studentId: string): Promise<Seat> {
  const res = await fetch(`${API_BASE}/api/classes/${classId}/hold`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ studentId })
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.detail || data.title || 'Failed to hold seat');
  }
  return data.data;
}

export async function confirmSeat(classId: string, studentId: string): Promise<Seat> {
  const res = await fetch(`${API_BASE}/api/classes/${classId}/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ studentId })
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.detail || data.title || 'Failed to confirm seat');
  }
  return data.data;
}

export async function cancelHold(classId: string, studentId: string): Promise<Seat> {
  const res = await fetch(`${API_BASE}/api/classes/${classId}/hold?studentId=${studentId}`, {
    method: 'DELETE'
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.detail || data.title || 'Failed to cancel hold');
  }
  return data.data;
}

export async function createClass(name: string, totalSeats: number, admissionFee: number): Promise<ClassItem> {
  const res = await fetch(`${API_BASE}/admin/classes`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, totalSeats, admissionFee })
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.detail || data.title || 'Failed to create class');
  }
  return data.data;
}
