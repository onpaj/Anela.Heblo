import { useMemo, useState } from "react";
import { Loader2, Search, X } from "lucide-react";
import { usePackingUsers } from "./usePackingUsers";
import { usePackingUser } from "./PackingUserContext";

export function PackingUserPicker() {
  const { isPickerOpen, current, setCurrent, closePicker } = usePackingUser();
  const { data: users, isLoading, isError } = usePackingUsers();
  const [query, setQuery] = useState("");

  const filtered = useMemo(() => {
    const list = users ?? [];
    const q = query.trim().toLowerCase();
    return q ? list.filter((u) => u.displayName.toLowerCase().includes(q)) : list;
  }, [users, query]);

  if (!isPickerOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-2xl rounded-xl bg-white p-6 shadow-xl">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-neutral-slate">Kdo balí?</h2>
          <button onClick={closePicker} aria-label="Zavřít" className="p-2 text-neutral-gray hover:text-neutral-slate">
            <X className="h-5 w-5" />
          </button>
        </div>

        {(users?.length ?? 0) > 8 && (
          <div className="mb-4 flex items-center gap-2 rounded-md border px-3 py-2">
            <Search className="h-4 w-4 text-neutral-gray" />
            <input
              autoFocus
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Hledat…"
              className="w-full outline-none"
            />
          </div>
        )}

        {isLoading && (
          <div className="flex items-center gap-2 py-8 text-neutral-gray">
            <Loader2 className="h-5 w-5 animate-spin" /> Načítám…
          </div>
        )}
        {isError && <p className="py-8 text-red-600">Nepodařilo se načíst seznam baličů.</p>}

        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {filtered.map((u) => (
            <button
              key={u.id}
              onClick={() => setCurrent({ id: u.id, displayName: u.displayName })}
              className={`rounded-lg border px-4 py-4 text-base font-medium transition-colors ${
                current?.id === u.id
                  ? "border-primary-blue bg-secondary-blue-pale text-primary-blue"
                  : "border-border-light hover:bg-secondary-blue-pale"
              }`}
            >
              {u.displayName}
            </button>
          ))}
        </div>
        {!isLoading && filtered.length === 0 && (
          <p className="py-8 text-center text-neutral-gray">Žádní baliči nejsou k dispozici.</p>
        )}
      </div>
    </div>
  );
}
