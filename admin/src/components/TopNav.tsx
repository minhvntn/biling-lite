import { NavLink } from 'react-router-dom';

function linkClass(isActive: boolean): string {
  return isActive ? 'top-nav-link top-nav-link-active' : 'top-nav-link';
}

export function TopNav() {
  return (
    <nav className="top-nav">
      <NavLink to="/pcs" className={({ isActive }) => linkClass(isActive)}>
        PCs
      </NavLink>
      <NavLink
        to="/history"
        className={({ isActive }) => linkClass(isActive)}
      >
        Session History
      </NavLink>
      <NavLink
        to="/members"
        className={({ isActive }) => linkClass(isActive)}
      >
        Hoi vien
      </NavLink>
    </nav>
  );
}
