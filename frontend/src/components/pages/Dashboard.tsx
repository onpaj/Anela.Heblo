import React, { useState } from 'react';
import ApiTestComponent from '../test/ApiTestComponent';

const Dashboard: React.FC = () => {
  const [activeTab, setActiveTab] = useState('listings');

  const mockBusinesses = [
    {
      id: 1,
      name: 'Anchor Oyster Bar',
      category: 'Seafood, +3',
      status: 'draft',
      rating: 3.0,
      reviews: 101,
      views: '313K',
      viewsChange: '+1%',
      actions: 630,
      actionsChange: '+5%',
      lastUpdate: '3 hours ago',
      image: '/api/placeholder/48/48'
    },
    {
      id: 2,
      name: 'Starbucks Coffee',
      category: 'Coffee & Tea, +3',
      status: 'published',
      rating: 3.5,
      reviews: 889,
      views: '193K',
      viewsChange: '+1%',
      actions: 189,
      actionsChange: '+5%',
      lastUpdate: '12/6/2017 8:23 PM',
      image: '/api/placeholder/48/48'
    },
    {
      id: 3,
      name: 'Kokkari Estiatorio',
      category: 'Greek, Mediterranean, +2',
      status: 'published',
      rating: 3.0,
      reviews: 381,
      views: '178K',
      viewsChange: '+1%',
      actions: 403,
      actionsChange: '+5%',
      lastUpdate: '16/2/2017 8:23 PM',
      image: '/api/placeholder/48/48'
    },
    {
      id: 4,
      name: 'Krispy Kreme',
      category: 'Donuts, Coffee & Tea, +3',
      status: 'published',
      rating: 4.0,
      reviews: 415,
      views: '881K',
      viewsChange: '+1%',
      actions: 199,
      actionsChange: '+5%',
      lastUpdate: '23/8/2016 8:23 PM',
      image: '/api/placeholder/48/48'
    },
    {
      id: 5,
      name: 'Eco Smart Landscaping',
      category: 'Contractors, Landscaping, +3',
      status: 'published',
      rating: 4.0,
      reviews: 415,
      views: '881K',
      viewsChange: '+1%',
      actions: 199,
      actionsChange: '+5%',
      lastUpdate: '23/8/2016 8:23 PM',
      image: '/api/placeholder/48/48'
    }
  ];

  const getStatusBadge = (status: string) => {
    if (status === 'published') {
      return (
        <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-emerald-100 text-emerald-800">
          Published
        </span>
      );
    }
    return (
      <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-600">
        Draft
      </span>
    );
  };

  const getRatingStars = (rating: number) => {
    const stars = [];
    for (let i = 1; i <= 5; i++) {
      if (i <= rating) {
        stars.push(
          <svg key={i} className="w-4 h-4 text-yellow-400 fill-current" viewBox="0 0 20 20">
            <path d="M10 15l-5.878 3.09 1.123-6.545L.489 6.91l6.572-.955L10 0l2.939 5.955 6.572.955-4.756 4.635 1.123 6.545z"/>
          </svg>
        );
      } else if (i - rating === 0.5) {
        stars.push(
          <svg key={i} className="w-4 h-4 text-yellow-400" viewBox="0 0 20 20">
            <defs>
              <linearGradient id={`half-${rating}`}>
                <stop offset="50%" stopColor="currentColor"/>
                <stop offset="50%" stopColor="transparent"/>
              </linearGradient>
            </defs>
            <path fill={`url(#half-${rating})`} d="M10 15l-5.878 3.09 1.123-6.545L.489 6.91l6.572-.955L10 0l2.939 5.955 6.572.955-4.756 4.635 1.123 6.545z"/>
          </svg>
        );
      } else {
        stars.push(
          <svg key={i} className="w-4 h-4 text-gray-300" viewBox="0 0 20 20">
            <path fill="currentColor" d="M10 15l-5.878 3.09 1.123-6.545L.489 6.91l6.572-.955L10 0l2.939 5.955 6.572.955-4.756 4.635 1.123 6.545z"/>
          </svg>
        );
      }
    }
    return stars;
  };

  return (
    <div className="max-w-7xl mx-auto">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Local Business</h1>
      </div>

      {/* API Test Component */}
      <div className="mb-8">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">API Test</h2>
        <ApiTestComponent />
      </div>

      {/* Tabs */}
      <div className="border-b border-gray-200 mb-6">
        <nav className="-mb-px flex space-x-8">
          <button
            onClick={() => setActiveTab('listings')}
            className={`py-2 px-1 border-b-2 font-medium text-sm ${
              activeTab === 'listings'
                ? 'border-indigo-500 text-indigo-600'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            Listings
          </button>
          <button
            onClick={() => setActiveTab('categories')}
            className={`py-2 px-1 border-b-2 font-medium text-sm ${
              activeTab === 'categories'
                ? 'border-indigo-500 text-indigo-600'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            Categories
          </button>
          <button
            onClick={() => setActiveTab('custom-fields')}
            className={`py-2 px-1 border-b-2 font-medium text-sm ${
              activeTab === 'custom-fields'
                ? 'border-indigo-500 text-indigo-600'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            Custom Fields
          </button>
        </nav>
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-6 mb-8">
        {/* Total Businesses */}
        <div className="bg-white p-6 rounded-lg border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <div className="w-8 h-8 bg-gray-100 rounded flex items-center justify-center">
                <svg className="w-5 h-5 text-gray-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
                </svg>
              </div>
            </div>
            <div className="ml-4">
              <p className="text-2xl font-bold text-gray-900">4,307</p>
              <p className="text-sm text-gray-500">Local Businesses</p>
            </div>
          </div>
        </div>

        {/* Verification Required */}
        <div className="bg-white p-6 rounded-lg border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <div className="w-8 h-8 bg-rose-100 rounded flex items-center justify-center">
                <svg className="w-5 h-5 text-rose-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.863-.833-2.633 0L4.138 14.5c-.77.833.192 2.5 1.732 2.5z" />
                </svg>
              </div>
            </div>
            <div className="ml-4">
              <p className="text-2xl font-bold text-gray-900">91</p>
              <p className="text-sm text-gray-500">Verification Required</p>
            </div>
          </div>
        </div>

        {/* Published */}
        <div className="bg-white p-6 rounded-lg border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <div className="w-8 h-8 bg-emerald-100 rounded flex items-center justify-center">
                <svg className="w-5 h-5 text-emerald-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                </svg>
              </div>
            </div>
            <div className="ml-4">
              <p className="text-2xl font-bold text-gray-900">67%</p>
              <p className="text-sm text-gray-500">Published</p>
            </div>
          </div>
        </div>
      </div>

      {/* Chart placeholder */}
      <div className="bg-white p-6 rounded-lg border border-gray-200 mb-8">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-medium text-gray-900">Activity Overview</h3>
          <div className="flex items-center space-x-4">
            <button className="text-sm text-gray-500 hover:text-gray-700">← August</button>
            <span className="text-sm font-medium text-gray-900">September 2018</span>
            <button className="text-sm text-gray-500 hover:text-gray-700">October →</button>
          </div>
        </div>
        
        {/* Simple bar chart representation */}
        <div className="flex items-end space-x-1 h-32">
          {[20, 35, 50, 30, 45, 60, 40, 55, 35, 45, 30, 40, 50, 35, 25, 45, 60, 40, 30, 35, 50, 45, 30, 40, 55, 35, 45, 50, 40, 30].map((height, index) => (
            <div
              key={index}
              className="bg-gray-300 rounded-sm flex-1"
              style={{ height: `${height}%` }}
            />
          ))}
        </div>
        
        <div className="mt-4 text-center">
          <span className="inline-flex items-center px-2 py-1 rounded text-xs font-medium bg-emerald-100 text-emerald-800">
            39 published
          </span>
        </div>
      </div>

      {/* Business List Table */}
      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        <div className="px-4 sm:px-6 py-4 border-b border-gray-200">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between">
            <h3 className="text-lg font-medium text-gray-900 mb-2 sm:mb-0">Business</h3>
            <div className="flex items-center space-x-2">
              <span className="text-sm text-gray-500">Status/Modified</span>
              <svg className="w-4 h-4 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
              </svg>
            </div>
          </div>
        </div>

        <div className="divide-y divide-gray-200">
          {mockBusinesses.map((business) => (
            <div key={business.id} className="px-4 sm:px-6 py-4 hover:bg-gray-50 transition-colors">
              <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between space-y-4 lg:space-y-0">
                {/* Business Info */}
                <div className="flex items-center space-x-4 flex-1">
                  <div className="w-12 h-12 bg-gray-200 rounded-lg flex-shrink-0"></div>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center space-x-2">
                      <h4 className="font-medium text-gray-900 truncate">{business.name}</h4>
                      <span className="w-5 h-5 bg-gray-600 text-white rounded-full flex items-center justify-center text-xs font-medium flex-shrink-0">
                        S
                      </span>
                    </div>
                    <p className="text-sm text-gray-500 truncate">{business.category}</p>
                    <p className="text-xs text-gray-400">{business.lastUpdate}</p>
                  </div>
                </div>

                {/* Status and Metrics */}
                <div className="flex flex-wrap items-center gap-4 lg:gap-8">
                  {/* Status */}
                  <div className="flex-shrink-0">
                    {getStatusBadge(business.status)}
                  </div>

                  {/* Rating - Hidden on mobile */}
                  <div className="text-center hidden sm:block">
                    <div className="flex items-center space-x-1">
                      {getRatingStars(business.rating)}
                    </div>
                    <p className="text-xs text-gray-500 mt-1">{business.reviews} reviews</p>
                  </div>

                  {/* Views */}
                  <div className="text-center min-w-0">
                    <p className="font-medium text-gray-900">{business.views}</p>
                    <p className="text-xs text-emerald-600">{business.viewsChange}</p>
                    <p className="text-xs text-gray-500">Total views</p>
                  </div>

                  {/* Actions - Hidden on small screens */}
                  <div className="text-center min-w-0 hidden md:block">
                    <p className="font-medium text-gray-900">{business.actions}</p>
                    <p className="text-xs text-rose-600">{business.actionsChange}</p>
                    <p className="text-xs text-gray-500">Total actions</p>
                  </div>

                  {/* Menu */}
                  <div className="flex items-center space-x-2 flex-shrink-0">
                    <button className="p-1 text-gray-400 hover:text-gray-600 transition-colors">
                      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" />
                      </svg>
                    </button>
                    <button className="p-1 text-gray-400 hover:text-gray-600 transition-colors">
                      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 5v.01M12 12v.01M12 19v.01M12 6a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2z" />
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};

export default Dashboard;