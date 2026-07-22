/**
 * Ivy Tendril Static Prototype App Controller
 * Manages view switching, interactive workflow graph, user feedback card blocking,
 * background jobs emulation, and tunnel live preview.
 */

class AppController {
  constructor() {
    this.currentStep = 1;
    this.currentView = 'plans';
    this.zoomLevel = 1.0;

    // Subagent workflow graph state
    this.nodes = [
      { id: 'leader', name: 'Agent 01: Orchestration Leader', role: 'Coordinator', status: 'complete', x: 220, y: 60, cost: '$0.004', tokens: '1,200', details: 'Decomposed prompt into microservice specs and scheduled parallel subagent tasks.' },
      { id: 'db', name: 'Agent 02: Database & Schema', role: 'Schema Architect', status: 'complete', x: 100, y: 180, cost: '$0.008', tokens: '2,400', details: 'Compiled SQLite schema for projects, issues, comments, and sprint boards.' },
      { id: 'backend', name: 'Agent 03: Backend API Agent', role: 'REST / GraphQL', status: 'blocked', x: 340, y: 180, cost: '$0.012', tokens: '3,800', details: 'Waiting for user feedback on issue tracking workflow model preference.' },
      { id: 'ui', name: 'Agent 04: Issue Tracker UI', role: 'Frontend React', status: 'running', x: 100, y: 310, cost: '$0.015', tokens: '4,100', details: 'Building drag-and-drop Kanban board components and filter widgets.' },
      { id: 'auth', name: 'Agent 05: Auth & Security', role: 'OAuth / SAML', status: 'pending', x: 340, y: 310, cost: '$0.005', tokens: '1,100', details: 'Pending completion of Backend API data schemas.' }
    ];

    this.edges = [
      { from: 'leader', to: 'db', active: false },
      { from: 'leader', to: 'backend', active: false },
      { from: 'db', to: 'ui', active: true },
      { from: 'backend', to: 'auth', active: false }
    ];

    // Background jobs emulation data
    this.jobs = [
      { id: 'job-leader-01', name: 'Agent 01: Orchestration Leader', status: 'complete', deps: 'None', duration: '1.2s', tokens: '1,200' },
      { id: 'job-db-schema-02', name: 'Agent 02: Database & Schema', status: 'complete', deps: 'job-leader-01', duration: '2.4s', tokens: '2,400' },
      { id: 'job-backend-api-03', name: 'Agent 03: Backend API Agent', status: 'blocked', deps: 'job-db-schema-02', duration: '3.8s', tokens: '3,800' },
      { id: 'job-react-ui-04', name: 'Agent 04: Issue Tracker UI', status: 'running', deps: 'job-db-schema-02', duration: '4.1s', tokens: '4,100' },
      { id: 'job-auth-jwt-05', name: 'Agent 05: Auth & Security', status: 'pending', deps: 'job-backend-api-03', duration: '0.0s', tokens: '1,100' }
    ];

    // File preview contents
    this.fileContents = {
      'schema.sql': `-- Generated Database Schema for Jira Prototype Clone
CREATE TABLE IF NOT EXISTS projects (
    id TEXT PRIMARY KEY,
    key TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS issues (
    id TEXT PRIMARY KEY,
    project_id TEXT REFERENCES projects(id),
    title TEXT NOT NULL,
    description TEXT,
    status TEXT CHECK(status IN ('todo', 'in_progress', 'review', 'done')),
    priority TEXT CHECK(priority IN ('low', 'medium', 'high', 'highest')),
    assignee TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);`,
      'routes.ts': `import { Router } from 'express';
export const router = Router();

// GET /api/v1/issues
router.get('/issues', async (req, res) => {
  const issues = await db.all('SELECT * FROM issues ORDER BY created_at DESC');
  res.json({ success: true, data: issues });
});

// POST /api/v1/issues/status
router.post('/issues/status', async (req, res) => {
  const { id, status } = req.body;
  await db.run('UPDATE issues SET status = ? WHERE id = ?', [status, id]);
  res.json({ success: true, updated: id });
});`,
      'KanbanBoard.tsx': `import React, { useState } from 'react';

export const KanbanBoard = ({ issues }) => {
  const columns = ['todo', 'in_progress', 'review', 'done'];
  return (
    <div className="grid grid-cols-4 gap-4 p-6">
      {columns.map(col => (
        <div key={col} className="bg-slate-900 rounded-lg p-4">
          <h3 className="font-bold uppercase text-slate-400 mb-3">{col}</h3>
          {issues.filter(i => i.status === col).map(issue => (
            <div key={issue.id} className="bg-slate-800 p-3 rounded mb-2 shadow">
              <span className="text-xs font-mono text-blue-400">{issue.id}</span>
              <h4 className="font-semibold text-sm mt-1">{issue.title}</h4>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
};`,
      'package.json': `{
  "name": "jira-clone-tendril",
  "version": "1.0.0",
  "private": true,
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "start": "node server.js"
  },
  "dependencies": {
    "express": "^4.19.2",
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "sqlite3": "^5.1.7"
  }
}`
    };

    this.init();
  }

  init() {
    this.renderJobsTable();
    this.renderConsoleLogs();
  }

  switchView(viewName) {
    this.currentView = viewName;

    // Update active nav items
    document.querySelectorAll('.nav-item').forEach(item => item.classList.remove('active'));
    const activeNav = document.getElementById(`nav-${viewName}`) || document.getElementById('nav-chat');
    if (activeNav) activeNav.classList.add('active');

    // Update view panels
    document.querySelectorAll('.view-panel').forEach(panel => panel.classList.remove('active'));
    const targetPanel = document.getElementById(`view-${viewName}`);
    if (targetPanel) targetPanel.classList.add('active');
  }

  jumpToStep(stepNum) {
    this.currentStep = stepNum;
    this.updateStepperUI();

    if (stepNum === 1) {
      this.switchView('plans');
    } else if (stepNum === 2) {
      this.switchView('plans');
      this.ensurePromptSubmitted();
    } else if (stepNum === 3) {
      this.switchView('plans');
      this.ensurePromptSubmitted();
      this.ensureQuestionVisible();
    } else if (stepNum === 4) {
      this.switchView('drafts');
    }
  }

  updateStepperUI() {
    document.querySelectorAll('.step-chip').forEach((chip, index) => {
      if (index + 1 === this.currentStep) {
        chip.classList.add('active');
      } else {
        chip.classList.remove('active');
      }
    });
  }

  triggerNewPlan() {
    this.resetDemo();
    this.submitPrompt('make me Jira');
  }

  submitPrompt(promptText) {
    const chatInput = document.getElementById('chat-input');
    if (chatInput) chatInput.value = promptText;
    this.handleInputSubmit();
  }

  handleInputSubmit() {
    const chatInput = document.getElementById('chat-input');
    if (!chatInput || !chatInput.value.trim()) return;

    const userText = chatInput.value.trim();
    chatInput.value = '';

    // Append user message
    this.appendUserMessage(userText);

    // Hide quick prompts
    const quickPrompts = document.getElementById('quick-prompts');
    if (quickPrompts) quickPrompts.classList.add('hidden');

    // Trigger agent breakdown thinking
    setTimeout(() => {
      this.appendAgentThinking();
      this.showWorkflowCanvas();
      this.renderGraphSVG();
      this.jumpToStep(2);
    }, 400);
  }

  appendUserMessage(text) {
    const chatHistory = document.getElementById('chat-history');
    if (!chatHistory) return;

    const msg = document.createElement('div');
    msg.className = 'chat-message user';
    msg.innerHTML = `
      <div class="msg-avatar">U</div>
      <div class="msg-content">
        <div class="msg-author">You</div>
        <div class="msg-text">${this.escapeHTML(text)}</div>
      </div>
    `;
    chatHistory.appendChild(msg);
    chatHistory.scrollTop = chatHistory.scrollHeight;
  }

  appendAgentThinking() {
    const chatHistory = document.getElementById('chat-history');
    if (!chatHistory) return;

    const msg = document.createElement('div');
    msg.className = 'chat-message agent';
    msg.id = 'agent-response-thinking';
    msg.innerHTML = `
      <div class="msg-avatar">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
      </div>
      <div class="msg-content">
        <div class="msg-author">Tendril Orchestrator</div>
        <div class="msg-text">
          Decomposing specification for <strong>Jira Clone Application</strong>...
          <div class="thinking-box">
            <div class="thinking-title">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"></path></svg>
              Spinning up 5 Subagents in Parallel
            </div>
            1. Orchestration Leader &rarr; Architecture Spec<br>
            2. Database Agent &rarr; SQLite Schema Compilation<br>
            3. Backend API Agent &rarr; REST Router & Workflows<br>
            4. Issue Tracker UI &rarr; React Kanban Board<br>
            5. Auth Agent &rarr; Session Security
          </div>
        </div>
      </div>
    `;
    chatHistory.appendChild(msg);
    chatHistory.scrollTop = chatHistory.scrollHeight;

    // Trigger feedback question after delay
    setTimeout(() => {
      this.appendFeedbackQuestion();
      this.jumpToStep(3);
    }, 1200);
  }

  appendFeedbackQuestion() {
    const chatHistory = document.getElementById('chat-history');
    if (!chatHistory || document.getElementById('feedback-card-element')) return;

    const msg = document.createElement('div');
    msg.className = 'chat-message agent';
    msg.id = 'feedback-card-element';
    msg.innerHTML = `
      <div class="msg-avatar">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>
      </div>
      <div class="msg-content">
        <div class="msg-author">Agent 03: Backend API Agent (Blocked)</div>
        <div class="feedback-card">
          <div class="feedback-header">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
            User Feedback Required (Blocks Main Flow)
          </div>
          <div class="feedback-body">
            <p>Which issue tracking workflow model would you prefer for the Jira API schema and UI columns?</p>
            <div class="options-group">
              <button class="option-btn selected" onclick="appController.selectOption(this)">1. Hybrid Scrum & Kanban (Recommended)</button>
              <button class="option-btn" onclick="appController.selectOption(this)">2. Pure Agile Scrum with Sprint Backlog</button>
              <button class="option-btn" onclick="appController.selectOption(this)">3. Basic Task List with Custom Tags</button>
            </div>
            <button class="btn btn-primary btn-sm" onclick="appController.submitFeedback()">Submit Feedback & Unblock Workflow</button>
          </div>
        </div>
      </div>
    `;
    chatHistory.appendChild(msg);
    chatHistory.scrollTop = chatHistory.scrollHeight;

    // Update node state to blocked
    const backendNode = this.nodes.find(n => n.id === 'backend');
    if (backendNode) backendNode.status = 'blocked';
    this.renderGraphSVG();
  }

  selectOption(btnElem) {
    const group = btnElem.closest('.options-group');
    if (!group) return;
    group.querySelectorAll('.option-btn').forEach(btn => btn.classList.remove('selected'));
    btnElem.classList.add('selected');
  }

  submitFeedback() {
    const card = document.getElementById('feedback-card-element');
    if (card) {
      card.querySelector('.feedback-card').innerHTML = `
        <div class="feedback-header" style="color: var(--accent-emerald);">
          ✓ Feedback Received & Workflow Resumed
        </div>
        <div class="feedback-body">
          <p style="color: var(--text-secondary); margin: 0;">Selected: <strong>Hybrid Scrum & Kanban</strong>. Backend API agent compiling routes...</p>
        </div>
      `;
    }

    // Unblock Backend and Auth agents
    const backendNode = this.nodes.find(n => n.id === 'backend');
    if (backendNode) backendNode.status = 'complete';

    const authNode = this.nodes.find(n => n.id === 'auth');
    if (authNode) authNode.status = 'complete';

    const uiNode = this.nodes.find(n => n.id === 'ui');
    if (uiNode) uiNode.status = 'complete';

    // Update background jobs
    this.jobs.forEach(j => j.status = 'complete');
    this.renderJobsTable();

    // Re-render SVG Graph
    this.renderGraphSVG();

    // Enable Draft Ready status
    const badgeDrafts = document.getElementById('badge-drafts');
    if (badgeDrafts) {
      badgeDrafts.innerText = '1 Ready';
      badgeDrafts.className = 'nav-badge badge-ready';
    }

    this.jumpToStep(4);
  }

  showWorkflowCanvas() {
    const emptyCanvas = document.getElementById('empty-canvas-state');
    if (emptyCanvas) emptyCanvas.classList.add('hidden');

    const graphContainer = document.getElementById('workflow-graph-container');
    if (graphContainer) graphContainer.classList.remove('hidden');
  }

  renderGraphSVG() {
    const svgEdges = document.getElementById('svg-edges');
    const svgNodes = document.getElementById('svg-nodes');
    if (!svgEdges || !svgNodes) return;

    svgEdges.innerHTML = '';
    svgNodes.innerHTML = '';

    // Render Edges
    this.edges.forEach(edge => {
      const fromNode = this.nodes.find(n => n.id === edge.from);
      const toNode = this.nodes.find(n => n.id === edge.to);
      if (!fromNode || !toNode) return;

      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      const startX = fromNode.x + 80;
      const startY = fromNode.y + 50;
      const endX = toNode.x + 80;
      const endY = toNode.y;

      const midY = (startY + endY) / 2;
      const d = `M ${startX} ${startY} C ${startX} ${midY}, ${endX} ${midY}, ${endX} ${endY}`;

      path.setAttribute('d', d);
      path.setAttribute('class', edge.active ? 'edge-line edge-active' : 'edge-line');
      path.setAttribute('marker-end', edge.active ? 'url(#arrowhead-active)' : 'url(#arrowhead)');
      svgEdges.appendChild(path);
    });

    // Render Nodes
    this.nodes.forEach(node => {
      const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      g.setAttribute('class', `svg-node node-${node.status}`);
      g.setAttribute('transform', `translate(${node.x}, ${node.y})`);
      g.onclick = () => this.inspectNode(node);

      let statusColor = '#3b82f6';
      let statusLabel = 'RUNNING';
      if (node.status === 'complete') { statusColor = '#10b981'; statusLabel = 'COMPLETE'; }
      if (node.status === 'blocked') { statusColor = '#f59e0b'; statusLabel = 'WAITING INPUT'; }
      if (node.status === 'pending') { statusColor = '#64748b'; statusLabel = 'PENDING'; }

      g.innerHTML = `
        <rect class="node-card-rect" width="160" height="54" />
        <circle cx="16" cy="18" r="5" fill="${statusColor}" />
        <text class="node-title-text" x="28" y="22">${node.name.split(':')[0]}</text>
        <text class="node-sub-text" x="16" y="42">${node.role}</text>
      `;

      svgNodes.appendChild(g);
    });
  }

  inspectNode(node) {
    const inspector = document.getElementById('node-inspector');
    const title = document.getElementById('inspector-title');
    const body = document.getElementById('inspector-body');

    if (!inspector || !title || !body) return;

    title.innerText = node.name;
    body.innerHTML = `
      <div style="margin-bottom: 8px;"><strong>Role:</strong> ${node.role}</div>
      <div style="margin-bottom: 8px;"><strong>Status:</strong> <span class="status-tag tag-${node.status}">${node.status.toUpperCase()}</span></div>
      <div style="margin-bottom: 8px;"><strong>Tokens Used:</strong> ${node.tokens}</div>
      <div style="margin-bottom: 8px;"><strong>Estimated Cost:</strong> ${node.cost}</div>
      <div style="margin-top: 12px; font-size: 11px; color: var(--text-muted);">${node.details}</div>
    `;

    inspector.classList.remove('hidden');
  }

  closeInspector() {
    const inspector = document.getElementById('node-inspector');
    if (inspector) inspector.classList.add('hidden');
  }

  renderJobsTable() {
    const tbody = document.getElementById('jobs-table-body');
    if (!tbody) return;

    tbody.innerHTML = this.jobs.map(job => `
      <tr>
        <td class="font-mono">${job.id}</td>
        <td><strong>${job.name}</strong></td>
        <td><span class="status-tag tag-${job.status}">${job.status.toUpperCase()}</span></td>
        <td class="font-mono text-muted">${job.deps}</td>
        <td>${job.duration}</td>
        <td>${job.tokens}</td>
        <td><button class="btn btn-secondary btn-sm" onclick="appController.inspectJobLogs('${job.id}')">View Logs</button></td>
      </tr>
    `).join('');
  }

  inspectJobLogs(jobId) {
    const tag = document.getElementById('console-job-tag');
    if (tag) tag.innerText = jobId;
    this.renderConsoleLogs(jobId);
  }

  renderConsoleLogs(jobId = 'job-backend-api-03') {
    const consoleOutput = document.getElementById('console-output');
    if (!consoleOutput) return;

    consoleOutput.innerHTML = `
[00:00:01.02] [INFO] Initializing promptware runner for ${jobId}...
[00:00:01.45] [DEBUG] Fetching repository references from ~/.tendril/Promptwares
[00:00:02.10] [INFO] Compiling dependency AST...
[00:00:03.20] [WARN] Decision point reached: Workflow model requires user specification.
[00:00:03.80] [PAUSED] Execution blocked pending user feedback submission.
    `;
  }

  filterJobs(filter) {
    document.querySelectorAll('.filter-tab').forEach(t => t.classList.remove('active'));
    event.target.classList.add('active');

    if (filter === 'all') {
      this.renderJobsTable();
    } else {
      const filtered = this.jobs.filter(j => j.status === filter);
      const tbody = document.getElementById('jobs-table-body');
      if (tbody) {
        tbody.innerHTML = filtered.map(job => `
          <tr>
            <td class="font-mono">${job.id}</td>
            <td><strong>${job.name}</strong></td>
            <td><span class="status-tag tag-${job.status}">${job.status.toUpperCase()}</span></td>
            <td class="font-mono text-muted">${job.deps}</td>
            <td>${job.duration}</td>
            <td>${job.tokens}</td>
            <td><button class="btn btn-secondary btn-sm" onclick="appController.inspectJobLogs('${job.id}')">View Logs</button></td>
          </tr>
        `).join('');
      }
    }
  }

  selectFile(fileName) {
    document.querySelectorAll('.file-item').forEach(item => item.classList.remove('active'));
    event.target.classList.add('active');

    const preview = document.getElementById('code-preview-content');
    if (preview && this.fileContents[fileName]) {
      preview.textContent = this.fileContents[fileName];
    }
  }

  spinUpTunnel() {
    const modal = document.getElementById('tunnel-modal');
    if (modal) modal.classList.remove('hidden');
  }

  closeTunnelModal() {
    const modal = document.getElementById('tunnel-modal');
    if (modal) modal.classList.add('hidden');
  }

  resetDemo() {
    this.currentStep = 1;
    this.updateStepperUI();
    this.switchView('plans');

    const chatHistory = document.getElementById('chat-history');
    if (chatHistory) {
      chatHistory.innerHTML = `
        <div class="chat-message agent">
          <div class="msg-avatar">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
          </div>
          <div class="msg-content">
            <div class="msg-author">Tendril Orchestrator</div>
            <div class="msg-text">
              Welcome! Describe the project or application you would like me to build. I will decompose your prompt, spin up parallel promptware agents, visualize the live state DAG on the right, and compile a draft repository.
            </div>
          </div>
        </div>
      `;
    }

    const quickPrompts = document.getElementById('quick-prompts');
    if (quickPrompts) quickPrompts.classList.remove('hidden');

    const graphContainer = document.getElementById('workflow-graph-container');
    if (graphContainer) graphContainer.classList.add('hidden');

    const emptyCanvas = document.getElementById('empty-canvas-state');
    if (emptyCanvas) emptyCanvas.classList.remove('hidden');

    this.nodes[2].status = 'blocked';
    this.renderGraphSVG();
  }

  ensurePromptSubmitted() {
    if (!document.getElementById('agent-response-thinking')) {
      this.submitPrompt('make me Jira');
    }
  }

  ensureQuestionVisible() {
    if (!document.getElementById('feedback-card-element')) {
      this.appendFeedbackQuestion();
    }
  }

  zoomGraph(factor) {
    this.zoomLevel *= factor;
    const svg = document.getElementById('workflow-svg');
    if (svg) svg.style.transform = `scale(${this.zoomLevel})`;
  }

  resetGraphZoom() {
    this.zoomLevel = 1.0;
    const svg = document.getElementById('workflow-svg');
    if (svg) svg.style.transform = `scale(1.0)`;
  }

  escapeHTML(str) {
    return str.replace(/[&<>'"]/g, 
      tag => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[tag] || tag)
    );
  }
}

// Global instance
const appController = new AppController();
