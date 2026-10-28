import { ReactNode } from 'react';
import { NavLink } from 'react-router-dom';

export interface TopNavProps {
  rightAction?: ReactNode;
}

function linkClass(isActive: boolean): string {
  return isActive ? 'top-nav-link top-nav-link-active' : 'top-nav-link';
}

export function TopNav({ rightAction }: TopNavProps) {
  return (
    <header className="top-header">
      <nav className="top-nav">
        <NavLink to="/pcs" className={({ isActive }) => linkClass(isActive)}>
          PCs
        </NavLink>
        <NavLink to="/dashboard" className={({ isActive }) => linkClass(isActive)}>
          Dashboard
        </NavLink>
        <NavLink
          to="/members"
          className={({ isActive }) => linkClass(isActive)}
        >
          Hội viên
        </NavLink>
      </nav>
      {rightAction && <div className="top-header-right">{rightAction}</div>}
    </header>
  );
}
