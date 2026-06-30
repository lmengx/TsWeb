import { get, post } from './api.js'

export const getUnverifiedList = () => get('/api/unverified/list')

export const getUnverifiedDetail = (nickname) =>
  get(`/api/unverified/detail?nickname=${encodeURIComponent(nickname)}`)

export const registerPlayer = (nickname, password, group) =>
  post('/api/unverified/register', { nickname, password, group })

export const forceLogin = (nickname) =>
  post('/api/unverified/force-login', { nickname })

export const kickUnverified = (nickname, reason) =>
  post('/api/unverified/kick', { nickname, reason })

export const banUnverified = (nickname, reason) =>
  post('/api/unverified/ban', { nickname, reason })
