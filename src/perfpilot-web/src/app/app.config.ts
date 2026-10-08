import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { RouteReuseStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { routes } from './app.routes';
import { apiInterceptor } from './core/http';
import { ParamAwareRouteReuseStrategy } from './core/route-reuse';

const PerfPilotTheme = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{indigo.50}',
      100: '{indigo.100}',
      200: '{indigo.200}',
      300: '{indigo.300}',
      400: '{indigo.400}',
      500: '{indigo.500}',
      600: '{indigo.600}',
      700: '{indigo.700}',
      800: '{indigo.800}',
      900: '{indigo.900}',
      950: '{indigo.950}',
    },
  },
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    { provide: RouteReuseStrategy, useClass: ParamAwareRouteReuseStrategy },
    provideHttpClient(withInterceptors([apiInterceptor])),
    providePrimeNG({ theme: { preset: PerfPilotTheme, options: { darkModeSelector: '.app-dark' } } }),
    MessageService,
    ConfirmationService,
  ],
};
