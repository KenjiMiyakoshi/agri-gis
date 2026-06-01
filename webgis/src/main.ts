import { createMap } from './map/mapInit';
import { wireLayerSelect } from './controllers/layer';
import { wireRotation } from './controllers/rotation';

const ctx = createMap('map');
wireRotation(ctx);
void wireLayerSelect(ctx);
