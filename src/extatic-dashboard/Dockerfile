FROM node:22-alpine AS build
WORKDIR /src

COPY src/extatic-dashboard/package.json src/extatic-dashboard/yarn.lock ./
RUN corepack enable && yarn install --frozen-lockfile

COPY src/extatic-dashboard/ ./
RUN yarn build

FROM nginx:1.30.6-alpine3.24
COPY src/extatic-dashboard/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/extatic-dashboard/browser/ /usr/share/nginx/html/
EXPOSE 8081
