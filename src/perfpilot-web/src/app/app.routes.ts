import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'scenarios' },
  {
    path: 'environments',
    loadComponent: () => import('./features/environments/environment-list').then((m) => m.EnvironmentListComponent),
    title: 'Environments · PerfPilot',
  },
  {
    path: 'environments/new',
    loadComponent: () => import('./features/environments/environment-form').then((m) => m.EnvironmentFormComponent),
    title: 'New environment · PerfPilot',
  },
  {
    path: 'environments/:id',
    loadComponent: () => import('./features/environments/environment-form').then((m) => m.EnvironmentFormComponent),
    title: 'Edit environment · PerfPilot',
  },
  {
    path: 'environments/:id/spec',
    loadComponent: () => import('./features/specs/spec-browser').then((m) => m.SpecBrowserComponent),
    title: 'API spec · PerfPilot',
  },
  {
    path: 'scenarios',
    loadComponent: () => import('./features/scenarios/scenario-list').then((m) => m.ScenarioListComponent),
    title: 'Scenarios · PerfPilot',
  },
  {
    path: 'scenarios/new',
    loadComponent: () => import('./features/scenarios/scenario-builder').then((m) => m.ScenarioBuilderComponent),
    title: 'New scenario · PerfPilot',
  },
  {
    path: 'scenarios/:id',
    loadComponent: () => import('./features/scenarios/scenario-builder').then((m) => m.ScenarioBuilderComponent),
    title: 'Scenario · PerfPilot',
  },
  {
    path: 'runs',
    loadComponent: () => import('./features/runs/run-list').then((m) => m.RunListComponent),
    title: 'Runs · PerfPilot',
  },
  {
    path: 'runs/:id',
    loadComponent: () => import('./features/runs/run-detail').then((m) => m.RunDetailComponent),
    title: 'Run results · PerfPilot',
  },
  { path: '**', redirectTo: 'scenarios' },
];
