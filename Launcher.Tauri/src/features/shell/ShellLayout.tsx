import { navItems } from "./navItems";

export function ShellLayout() {
  return (
    <div className="app-shell">
      <aside>
        {navItems.map((item) => (
          <button key={item} type="button">
            {item}
          </button>
        ))}
      </aside>
      <main />
    </div>
  );
}
