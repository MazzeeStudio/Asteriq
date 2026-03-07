import React from 'react';
import ComponentCreator from '@docusaurus/ComponentCreator';

export default [
  {
    path: '/Asteriq/docs/',
    component: ComponentCreator('/Asteriq/docs/', 'f81'),
    routes: [
      {
        path: '/Asteriq/docs/',
        component: ComponentCreator('/Asteriq/docs/', '49f'),
        routes: [
          {
            path: '/Asteriq/docs/',
            component: ComponentCreator('/Asteriq/docs/', 'f60'),
            routes: [
              {
                path: '/Asteriq/docs/client-only',
                component: ComponentCreator('/Asteriq/docs/client-only', 'e2e'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/Asteriq/docs/devices',
                component: ComponentCreator('/Asteriq/docs/devices', '45d'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/Asteriq/docs/getting-started',
                component: ComponentCreator('/Asteriq/docs/getting-started', '9a7'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/Asteriq/docs/mappings',
                component: ComponentCreator('/Asteriq/docs/mappings', '5d9'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/Asteriq/docs/network-forwarding',
                component: ComponentCreator('/Asteriq/docs/network-forwarding', '0db'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/Asteriq/docs/sc-bindings',
                component: ComponentCreator('/Asteriq/docs/sc-bindings', 'fc4'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/Asteriq/docs/settings',
                component: ComponentCreator('/Asteriq/docs/settings', '93f'),
                exact: true,
                sidebar: "docsSidebar"
              },
              {
                path: '/Asteriq/docs/',
                component: ComponentCreator('/Asteriq/docs/', '191'),
                exact: true,
                sidebar: "docsSidebar"
              }
            ]
          }
        ]
      }
    ]
  },
  {
    path: '*',
    component: ComponentCreator('*'),
  },
];
