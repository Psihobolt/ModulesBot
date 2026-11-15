import type { WebModule } from '../../platform/router';
import './movie-night.css';
import { resolveStartParam, routes } from './routes';

export const movieNightModule: WebModule = {
  name: 'movienight',
  title: 'Киновечер',
  basePath: '/movienight',
  routes,
  resolveStartParam,
};
