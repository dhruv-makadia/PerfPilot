import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, BaseRouteReuseStrategy } from '@angular/router';

/**
 * Recreates a routed component when its route params change (e.g. /runs/a → /runs/b after "Run again",
 * or /scenarios/a → /scenarios/b after cloning), so pages always load the entity in the URL.
 */
@Injectable()
export class ParamAwareRouteReuseStrategy extends BaseRouteReuseStrategy {
  override shouldReuseRoute(future: ActivatedRouteSnapshot, curr: ActivatedRouteSnapshot): boolean {
    return future.routeConfig === curr.routeConfig && JSON.stringify(future.params) === JSON.stringify(curr.params);
  }
}
